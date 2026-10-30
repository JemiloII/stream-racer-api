// Uma drivers (game 2.7 seats a driver in every car): a racer who never customised their driver, or who picked a girl
// with !race uma, gets that Umamusume in the seat instead. The game's seated driver stays built but is hidden; she sits
// in its place (same spot and size, legs to the pedals, hands on the wheel). Cosmetic, so ranked allows it.
using System.IO;

namespace StreamRacerApi;

static partial class Chaos
{
    const string UmaDriverName = "sr-uma-driver";
    const float LegSlack = 1.15f;      // leg length / seat-to-pedals distance: knees stay a little bent
    const float HipAboveSeat = 0.06f;  // her hip joint sits this far above the game's seat point
    const float EarTipAllowance = 0.04f;   // the ear mesh runs this far (model metres) past its last bone

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

    // The game's seat for this car model (body "b" variant when there are two): seat point, wheel grips, pedals.
    static Cage.StreamRacer.Drivers.DriverPose SeatPoseOf(Vehicle vehicle)
    {
        var seats = RenamedMethods.DriverLibrary()?.VehicleSeats;
        int type = (int)vehicle.Type();
        var variants = seats != null && type < seats.Length ? seats[type]?.Variants : null;
        return variants == null || variants.Length == 0 ? null : variants[variants.Length - 1];
    }
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
        pose = SeatPoseOf(vehicle) is Cage.StreamRacer.Drivers.DriverPose pose
            ? $"pos {pose.Position} wrapPos {pose.WrapperPosition} root {pose.RootPosition} scale {pose.Scale} wrapScale {pose.WrapperScale} seat {pose.SeatPoint} grips {pose.LeftGrip} {pose.RightGrip} pedals {pose.LeftPedal} {pose.RightPedal}"
            : null,
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
        if (current == null && !BuildUmaDriver(seat, key, vehicle))
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

    static bool BuildUmaDriver(Transform seat, string key, Vehicle vehicle)
    {
        var box = GameDriverBox(seat);
        var pose = SeatPoseOf(vehicle);
        var template = box == null || pose == null ? null : UmaTemplateFor(key);
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
        SeatOnPose(uma.transform, seat, pose, box.Value.max.y);
        if (face != null)
        {
            holder.gameObject.AddComponent<UmaDriverFace>().Feed = face;
        }
        return true;
    }

    // The game's seat pose (seat-node space): hips on its seat point, sized so her legs reach its pedals, ears under
    // the game driver's head line, then legs bent to the pedals and hands on the wheel grips.
    static void SeatOnPose(Transform uma, Transform seat, Cage.StreamRacer.Drivers.DriverPose pose, float headLine)
    {
        var bones = uma.GetComponentsInChildren<Transform>(true)
            .GroupBy(bone => bone.name)
            .ToDictionary(group => group.Key, group => group.First());
        Transform Bone(string name) => bones.TryGetValue(name, out var bone) ? bone : null;
        var hip = Bone("Hip");
        if (hip == null || Bone("Thigh_L") == null || Bone("Knee_L") == null || Bone("Ankle_L") == null)
        {
            return;
        }
        var holder = uma.parent;
        float leg = Vector3.Distance(Bone("Thigh_L").position, Bone("Knee_L").position)
            + Vector3.Distance(Bone("Knee_L").position, Bone("Ankle_L").position);
        float reach = Vector3.Distance(pose.SeatPoint, (pose.LeftPedal + pose.RightPedal) * 0.5f);
        uma.localScale *= reach * LegSlack / Mathf.Max(0.01f, holder.InverseTransformVector(Vector3.up * leg).magnitude);
        uma.localPosition += pose.SeatPoint + Vector3.up * HipAboveSeat - holder.InverseTransformPoint(hip.position);
        KeepEarsUnder(uma, hip, headLine);
        // which of her sides is the seat's left (-x): her _L bones may sit on either side of the model
        bool leftIsMinusX = holder.InverseTransformPoint(Bone("Thigh_L").position).x < holder.InverseTransformPoint(Bone("Thigh_R").position).x;
        foreach (var side in new[] { "L", "R" })
        {
            bool seatLeft = (side == "L") == leftIsMinusX;
            var pedal = seat.TransformPoint(seatLeft ? pose.LeftPedal : pose.RightPedal);
            var grip = seat.TransformPoint(seatLeft ? pose.LeftGrip : pose.RightGrip);
            var outward = seat.TransformDirection(seatLeft ? Vector3.left : Vector3.right);
            var thigh = Bone("Thigh_" + side);
            ReachWith(thigh, Bone("Knee_" + side), Bone("Ankle_" + side), pedal,
                thigh.position + seat.TransformDirection(Vector3.forward + Vector3.up));
            var arm = Bone("Arm_" + side);
            ReachWith(arm, Bone("Elbow_" + side), Bone("Wrist_" + side), grip,
                arm.position + outward * 0.5f - seat.up * 0.5f);
        }
    }

    // Two-bone IK: turns root and mid so `end` lands on `target`, bending toward `pole` (all world space).
    static void ReachWith(Transform root, Transform mid, Transform end, Vector3 target, Vector3 pole)
    {
        if (root == null || mid == null || end == null)
        {
            return;
        }
        var start = root.position;
        float upper = Vector3.Distance(start, mid.position);
        float lower = Vector3.Distance(mid.position, end.position);
        var toTarget = target - start;
        float distance = Mathf.Clamp(toTarget.magnitude, 0.01f, (upper + lower) * 0.999f);
        var direction = toTarget.normalized;
        var bend = Vector3.ProjectOnPlane(pole - start, direction);
        bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.up;
        float cosine = Mathf.Clamp((upper * upper + distance * distance - lower * lower) / (2f * upper * distance), -1f, 1f);
        var joint = start + direction * (upper * cosine) + bend * (upper * Mathf.Sqrt(1f - cosine * cosine));
        Aim(root, mid, joint - start);
        Aim(mid, end, start + direction * distance - mid.position);
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

    // Her ears stand above her crown: shrink her about her hips until the tips are under the game driver's head line
    // (roughly where the roof is), so they don't poke through it.
    static void KeepEarsUnder(Transform uma, Transform hip, float top)
    {
        var holder = uma.parent;
        var tips = uma.GetComponentsInChildren<Transform>(true).Where(bone => bone.name.StartsWith("Ear_03_")).ToList();
        if (tips.Count == 0)
        {
            return;
        }
        float hipY = holder.InverseTransformPoint(hip.position).y;
        float tipY = tips.Max(tip => holder.InverseTransformPoint(tip.position).y) + EarTipAllowance * uma.localScale.y;
        if (tipY <= top || tipY - hipY < 0.01f)
        {
            return;
        }
        float shrink = (top - hipY) / (tipY - hipY);
        var hipBefore = holder.InverseTransformPoint(hip.position);
        uma.localScale *= shrink;
        uma.localPosition += hipBefore - holder.InverseTransformPoint(hip.position);
    }

    // Her face material needs the head's pose every frame (real look only).
    class UmaDriverFace : MonoBehaviour
    {
        public System.Action Feed;

        void LateUpdate() => Feed?.Invoke();
    }
}
