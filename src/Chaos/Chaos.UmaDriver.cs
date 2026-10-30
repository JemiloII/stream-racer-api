// Uma drivers (game 2.7 seats a driver in every car): a racer who never customised their driver, or who picked a girl
// with !race uma, gets that Umamusume in the seat instead. The game's seated driver stays built but is hidden; she sits
// in its place (same spot and size, legs to the pedals, hands on the wheel). Cosmetic, so ranked allows it.
using System.IO;

namespace StreamRacerApi;

static partial class Chaos
{
    const string UmaDriverName = "sr-uma-driver";
    const float SeatedShare = 0.62f;   // her seated height (hips to crown) as a share of the game driver's box height

    // Which girl drives this car: her own pick, else a fixed girl per login when the racer kept the default driver,
    // else nobody (their customised driver stays).
    static string UmaDriverFor(Vehicle vehicle)
    {
        string login = Game.Login(vehicle) ?? "";
        if (Settings.Current.umas.TryGetValue(login, out var picked) && File.Exists(Path.Combine(UmaFolder, picked, "model.json")))
        {
            return picked;
        }
        if (HasCustomDriver(login))
        {
            return null;
        }
        var keys = UmaKeys();
        return keys.Count == 0 ? null : keys[(int)(Pure.StableHash(login) % (uint)keys.Count)];
    }

    static float _nextUmaDriverCheck;
    static readonly Dictionary<string, Cage.StreamRacer.Drivers.DriverPortrait> _portraits = new();

    // ponytail: the backend generates every racer a driver (portrait revision 1); each save on the game's site bumps the
    // revision, so above 1 = customised. Breaks if the backend ever starts generated portraits at another revision.
    static bool HasCustomDriver(string login) => _portraits.TryGetValue(login, out var portrait) && portrait != null && portrait.Revision > 1;

    public static void NotePortrait(RacerProfile profile, Cage.StreamRacer.Drivers.DriverPortrait portrait)
    {
        if (profile?.Login() != null)
        {
            _portraits[profile.Login()] = portrait;
        }
    }

    // dev aid (GET /find/drivers): who gets an Uma driver and why
    public static object DriverReport() => Game.Vehicles().Select(vehicle => new
    {
        login = Game.Login(vehicle),
        car = vehicle.Car()?.name,
        seat = vehicle.Car()?.transform.Find("Driver") != null,
        custom = HasCustomDriver(Game.Login(vehicle) ?? ""),
        portrait = _portraits.TryGetValue(Game.Login(vehicle) ?? "", out var portrait) ? Newtonsoft.Json.JsonConvert.SerializeObject(portrait) : "none",
        uma = UmaDriverFor(vehicle),
        box = vehicle.Car()?.transform.Find("Driver") is Transform seat ? GameDriverBox(seat)?.ToString() : null,
    }).ToList();

    static partial void TickUmaDrivers()
    {
        if (Time.unscaledTime < _nextUmaDriverCheck)
        {
            return;
        }
        _nextUmaDriverCheck = Time.unscaledTime + 0.5f;
        foreach (var vehicle in Game.Vehicles())
        {
            if (vehicle.Car() == null)
            {
                continue;
            }
            if (_costumes.ContainsKey(vehicle))
            {
                var seated = vehicle.Car().transform.Find("Driver/" + UmaDriverName);
                if (seated != null)
                {
                    Object.Destroy(seated.gameObject);   // the whole car is a girl now (Uma race / costume)
                }
                continue;
            }
            Guard("uma driver", () => SeatUmaDriver(vehicle));
        }
    }

    static void SeatUmaDriver(Vehicle vehicle)
    {
        var seat = vehicle.Car().transform.Find("Driver");
        if (seat == null)
        {
            return;
        }
        string key = UmaDriverFor(vehicle);
        var current = seat.Find(UmaDriverName);
        if (current != null && current.GetChild(0).name != key)
        {
            Object.Destroy(current.gameObject);   // she picked another girl mid-race
            current = null;
        }
        if (key == null)
        {
            if (current != null)
            {
                ShowGameDriver(seat, true);
            }
            return;
        }
        if (current == null && !BuildUmaDriver(seat, key))
        {
            return;
        }
        if (current == null)
        {
            Plugin.Log.LogInfo("uma driver: " + Game.Login(vehicle) + " -> " + key);
        }
        ShowGameDriver(seat, false);   // every tick: the game rebuilds its driver (appearance updates, respawns)
    }

    static bool IsOurs(Transform part, Transform seat)
    {
        var mine = seat.Find(UmaDriverName);
        return mine != null && part.IsChildOf(mine);
    }

    static void ShowGameDriver(Transform seat, bool show)
    {
        foreach (var renderer in seat.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsOurs(renderer.transform, seat))
            {
                renderer.forceRenderingOff = !show;
            }
        }
    }

    // The game driver's box in seat space, from its baked seated mesh; null until the game has built it.
    static Bounds? GameDriverBox(Transform seat)
    {
        Bounds? box = null;
        foreach (var filter in seat.GetComponentsInChildren<MeshFilter>(false))
        {
            if (filter.sharedMesh == null || IsOurs(filter.transform, seat))
            {
                continue;
            }
            var bounds = filter.sharedMesh.bounds;
            foreach (var corner in Corners(bounds))
            {
                var point = seat.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (box == null)
                {
                    box = new Bounds(point, Vector3.zero);
                }
                else
                {
                    var grown = box.Value;
                    grown.Encapsulate(point);
                    box = grown;
                }
            }
        }
        return box;
    }

    static IEnumerable<Vector3> Corners(Bounds bounds)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            yield return new Vector3(
                (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
        }
    }

    static bool BuildUmaDriver(Transform seat, string key)
    {
        var box = GameDriverBox(seat);
        var template = box == null ? null : UmaTemplateFor(key);
        if (template == null)
        {
            return false;
        }
        var holder = new GameObject(UmaDriverName).transform;
        holder.SetParent(seat, false);
        var uma = Object.Instantiate(template.Model, holder, false);
        uma.name = key;
        uma.SetActive(true);
        NeutralAmbient(uma);
        OnUmaLayer(uma);
        System.Action face = template.Real ? RealLookFor(uma, null) : null;
        StandingPose(uma.transform, key);
        SitDown(uma.transform);
        FitToSeat(uma.transform, box.Value);
        if (face != null)
        {
            holder.gameObject.AddComponent<UmaDriverFace>().Feed = face;
        }
        return true;
    }

    // Seated: thighs forward, shins down to the pedals, upper arms forward and down, forearms up to the wheel.
    static void SitDown(Transform uma)
    {
        var bones = uma.GetComponentsInChildren<Transform>(true)
            .GroupBy(bone => bone.name)
            .ToDictionary(group => group.Key, group => group.First());
        Transform Bone(string name) => bones.TryGetValue(name, out var bone) ? bone : null;
        Vector3 Local(float x, float y, float z) => uma.TransformDirection(new Vector3(x, y, z).normalized);
        foreach (var side in new[] { ("L", 1f), ("R", -1f) })
        {
            float outward = side.Item2;   // +x is her left
            Aim(Bone("Thigh_" + side.Item1), Bone("Knee_" + side.Item1), Local(0.1f * outward, 0.08f, 1f));
            Aim(Bone("Knee_" + side.Item1), Bone("Ankle_" + side.Item1), Local(0f, -0.55f, 1f));
            Aim(Bone("Arm_" + side.Item1), Bone("Elbow_" + side.Item1), Local(0.25f * outward, -0.75f, 0.6f));
            Aim(Bone("Elbow_" + side.Item1), Bone("Wrist_" + side.Item1), Local(-0.3f * outward, 0.35f, 1f));
        }
    }

    // Turn `bone` so its child lies along `direction` (world).
    static void Aim(Transform bone, Transform child, Vector3 direction)
    {
        if (bone == null || child == null)
        {
            return;
        }
        bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;
    }

    // Same size and spot as the game's driver: crown at the top of its box, hips centred over its seat.
    static void FitToSeat(Transform uma, Bounds box)
    {
        var hip = uma.GetComponentsInChildren<Transform>(true).FirstOrDefault(bone => bone.name == "Hip");
        var head = uma.GetComponentsInChildren<Transform>(true).FirstOrDefault(bone => bone.name == "Head");
        if (hip == null || head == null)
        {
            return;
        }
        var holder = uma.parent;
        float seated = holder.InverseTransformPoint(head.position).y - holder.InverseTransformPoint(hip.position).y;
        uma.localScale *= box.size.y * SeatedShare / Mathf.Max(0.01f, seated * 1.25f);   // head bone sits ~1/5 below her crown
        var hipNow = holder.InverseTransformPoint(hip.position);
        var headNow = holder.InverseTransformPoint(head.position);
        float crown = headNow.y + (headNow.y - hipNow.y) * 0.25f;
        uma.localPosition += new Vector3(box.center.x - hipNow.x, box.max.y - crown, box.center.z - hipNow.z);
    }

    // Her face material needs the head's pose every frame (real look only).
    class UmaDriverFace : MonoBehaviour
    {
        public System.Action Feed;

        void LateUpdate() => Feed?.Invoke();
    }
}
