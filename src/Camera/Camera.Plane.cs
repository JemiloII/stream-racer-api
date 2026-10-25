// Cam plane cam: while an air raid (Chaos.Raid) is on, the camera rides behind the lead bomber through its dives, so
// the raid can be watched. It is temporary and easy to leave: any key, mouse click or scroll in the game, the "Plane
// cam off" button (POST /camera/plane/off) or the raid ending puts the camera back exactly as it was (the game's
// follow cam on the same car, the free cam where it stood, or the director). settings.camera.shots.plane = false
// turns it off. The black hole borrows it (label "black hole", fixed `from`): the camera stands at one spot and
// watches a swallowed car fly up into the hole.

namespace StreamRacerApi;

static partial class Cam
{
    const float PlaneBehind = 22f, PlaneAbove = 5f, PlaneLookAhead = 30f;

    static Transform _plane;
    static Vector3? _planeFrom;                 // set: stand here and look at _plane instead of riding behind it
    static CameraMode _beforePlaneMode;
    static Vehicle _beforePlaneFollow;          // the car the game's follow cam was on, if it was on one
    static bool _beforePlaneFree;
    static Vector3 _beforePlanePosition;
    static Quaternion _beforePlaneRotation;

    public static bool OnPlane => Mode == CameraMode.Plane;

    public static bool OnEventCam(string label) => OnPlane && Target == label;

    // Ride behind `plane` (or, with `from`, watch it from there) until EndPlaneCam. A second call just switches.
    public static bool PlaneCam(Transform plane, string label = "air raid", Vector3? from = null)
    {
        _planeFrom = from;
        if (plane == null || !On("plane") || !Ready)
        {
            return false;
        }
        if (OnPlane)
        {
            _plane = plane;
            Target = label;
            return true;
        }
        RememberBeforePlane();
        if (!Begin(CameraMode.Plane, null, 0f, 0f, 0f))
        {
            return false;
        }
        _plane = plane;
        Target = label;
        _autoPausedUntil = float.MaxValue;   // the director waits
        return true;
    }

    // Back to what the camera was doing before the plane cam. Does nothing when the plane cam isn't on.
    public static void EndPlaneCam()
    {
        if (!OnPlane)
        {
            return;
        }
        _plane = null;
        RestoreFov();
        Mode = CameraMode.None;
        Target = null;
        _shotLogins = new List<string>();
        _autoPausedUntil = 0f;
        if (_beforePlaneFollow?.Car() != null && Instances.FollowCam != null)
        {
            Instances.FreeCam?.SetActive(false);
            Instances.FollowCam.Follow(_beforePlaneFollow);
            if (_beforePlaneMode is CameraMode.Follow or CameraMode.FollowWide)
            {
                Mode = _beforePlaneMode;
                Target = Game.Login(_beforePlaneFollow);
            }
        }
        else if (_beforePlaneFree && Instances.FreeCam != null)
        {
            Instances.FreeCam.transform.SetPositionAndRotation(_beforePlanePosition, _beforePlaneRotation);
            if (_beforePlaneMode == CameraMode.Free)
            {
                Mode = CameraMode.Free;
            }
        }
        Announce();
    }

    static void RememberBeforePlane()
    {
        _beforePlaneMode = Mode;
        _beforePlaneFollow = null;
        var followed = Instances.FollowCam != null && Instances.FollowCam.IsActive() ? Instances.FollowCam.Target() : null;
        if (followed != null)
        {
            _beforePlaneFollow = Game.Vehicles().FirstOrDefault(vehicle => vehicle.Car() != null
                && (followed == vehicle.Car().transform || followed.IsChildOf(vehicle.Car().transform)));
        }
        _beforePlaneFree = Instances.FreeCam != null && Instances.FreeCam.IsActive();
        if (_beforePlaneFree)
        {
            _beforePlanePosition = Instances.FreeCam.transform.position;
            _beforePlaneRotation = Instances.FreeCam.transform.rotation;
        }
    }

    // Tick's plane case: behind and a little above the plane, looking down its flight path (so the dive shows the car).
    static bool PlaneShot(out Vector3 wanted, out Vector3 lookAt)
    {
        wanted = lookAt = Vector3.zero;
        if (_plane == null)
        {
            return false;
        }
        if (_planeFrom.HasValue)
        {
            wanted = _planeFrom.Value;
            lookAt = _plane.position;
            return true;
        }
        var forward = _plane.forward;
        wanted = _plane.position - forward * PlaneBehind + Vector3.up * PlaneAbove;
        lookAt = _plane.position + forward * PlaneLookAhead;
        return true;
    }

    // Plugin.Update: the streamer pressing anything in the game hands the camera back at once.
    public static void PlaneCamInput()
    {
        if (OnPlane && (Input.anyKeyDown || Input.GetAxisRaw("Mouse ScrollWheel") != 0f))
        {
            EndPlaneCam();
        }
    }
}
