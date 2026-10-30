// Burning oil: while a dragon is out, its fireballs go for the game's own oil spills first (game 2.7 chaos effect).
// A hit spill bursts into flames that burn on (even after the game clears the oil), then die down.
using System.Collections;

namespace StreamRacerApi;

static partial class Chaos
{
    const float OilTargetRange = 140f;   // a dragon only goes for spills this close to it
    const float OilFireSeconds = 20f;    // a lit spill burns this long, then dies down over OilFireFade
    const float OilFireFade = 4f;
    static readonly HashSet<int> _litSpills = new();

    // The nearest spill on the road that isn't burning yet, or null.
    static Transform UnlitSpillNear(Vector3 point) => Object.FindObjectsOfType<OilSpill>()
        .Where(spill => spill.isActiveAndEnabled && !_litSpills.Contains(spill.GetInstanceID()))
        .Select(spill => spill.transform)
        .Where(spill => Vector3.Distance(spill.position, point) < OilTargetRange)
        .OrderBy(spill => Vector3.Distance(spill.position, point))
        .FirstOrDefault();

    // A fireball like the one for cars, flying to the spill's spot; landing sets it alight.
    static bool ShootSpill(DragonRig rig)
    {
        var spill = rig.Mouth == null ? null : UnlitSpillNear(rig.Mouth.position);
        if (spill == null)
        {
            return false;
        }
        _litSpills.Add(spill.GetComponent<OilSpill>().GetInstanceID());
        var area = SpillArea(spill);
        var breath = Puff(rig.Mouth, FireballColor, FlameEnd, 25, 1.2f, 6f, 0.5f);
        Object.Destroy(breath.gameObject, 1.5f);
        var shot = Holder("fireball");
        shot.position = rig.Mouth.position;
        Prim(PrimitiveType.Sphere, shot, Vector3.zero, Vector3.one * 1.3f, FireballColor, glow: true);
        Prim(PrimitiveType.Sphere, shot, Vector3.zero, Vector3.one * 0.8f, FireballCore, glow: true);
        var trail = Stream(shot, FireballColor, FlameEnd, 70f, 0.9f, 0.6f, 0.5f);
        Spawn(FlyToSpill(shot, area, trail));
        return true;
    }

    static IEnumerator FlyToSpill(Transform shot, Bounds area, ParticleSystem trail)
    {
        for (float time = 0f; time < 8f && shot != null; time += Time.deltaTime)
        {
            shot.position = Vector3.MoveTowards(shot.position, area.center, FireballSpeed * Time.deltaTime);
            if (Vector3.Distance(shot.position, area.center) < 0.8f)
            {
                LightSpill(area);
                break;
            }
            yield return null;
        }
        if (shot != null)
        {
            FireballFizzle(shot.position, trail);
            Object.Destroy(shot.gameObject);
        }
    }

    // The spill's footprint on the road (its renderers), or a 3 m patch round it when it draws nothing we can measure.
    static Bounds SpillArea(Transform spill)
    {
        var renderers = spill.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(spill.position, new Vector3(3f, 0.2f, 3f));
        }
        var area = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
        {
            area.Encapsulate(renderer.bounds);
        }
        return area;
    }

    static IEnumerator BurnOut(Transform fire, Light glow)
    {
        yield return new WaitForSeconds(OilFireSeconds);
        if (fire == null)
        {
            yield break;
        }
        foreach (var system in fire.GetComponentsInChildren<ParticleSystem>())
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        for (float time = 0f; time < OilFireFade && glow != null; time += Time.deltaTime)
        {
            glow.intensity = FireLightIntensity * (1f - time / OilFireFade);
            yield return null;
        }
        if (fire != null)
        {
            Object.Destroy(fire.gameObject);
        }
    }

    // Flames spread over the spill, with a glow; on the chaos root, so race end / a clear also puts them out.
    static void LightSpill(Bounds area)
    {
        var fire = Holder("sr-oil-fire");
        fire.position = new Vector3(area.center.x, area.min.y + 0.1f, area.center.z);
        int flames = Mathf.Clamp(Mathf.RoundToInt(area.size.x * area.size.z / 3f), 3, 10);
        for (int flame = 0; flame < flames; flame++)
        {
            var spot = Holder("flame", fire);
            spot.localPosition = new Vector3(
                Random.Range(-area.extents.x, area.extents.x) * 0.8f,
                0f,
                Random.Range(-area.extents.z, area.extents.z) * 0.8f);
            Rise(Stream(spot, FlameColor, FlameEnd, FlameRate, 1.1f, 1.4f, 0.8f));
            if (flame % 3 == 0)
            {
                Rise(Stream(spot, SmokeColor, SmokeEnd, FlameRate * 0.25f, 1.6f, 1.6f, 1.8f));
            }
        }
        var glow = fire.gameObject.AddComponent<Light>();
        glow.color = FlameColor;
        glow.range = FireLightRange * 1.5f;
        glow.intensity = FireLightIntensity;
        Spawn(BurnOut(fire, glow));
        Plugin.Emit("fire", new
        {
            login = (string)null,
            phase = "oil",
            seconds = 0f,
        });
    }
}
