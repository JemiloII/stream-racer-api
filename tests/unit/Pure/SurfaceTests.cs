// Track surfaces: route-frame maths, patch hit tests, snow packing, mud drag and placement (Pure surfaces section).
using System.Linq;
using UnityEngine;
using Xunit;
using static StreamRacerApi.Pure;

namespace StreamRacerApi.Tests.Pure;

public class SurfaceTests
{
    static SurfacePatch Stretch(SurfaceKind kind, float start, float end, float halfWidth = 7f) => new SurfacePatch
    {
        Kind = kind,
        Start = start,
        End = end,
        HalfWidth = halfWidth,
    };

    static SurfacePatch Puddle(float start, float end, float lateral, float halfWidth) => new SurfacePatch
    {
        Kind = SurfaceKind.Puddle,
        Oval = true,
        Start = start,
        End = end,
        Lateral = lateral,
        HalfWidth = halfWidth,
    };

    [Fact]
    public void Route_frame_splits_an_offset_into_along_and_right_of_travel()
    {
        var (along, lateral) = RouteFrame(new Vector3(3f, 5f, 10f), Vector3.zero, Vector3.forward);
        Assert.Equal(10f, along, 3);
        Assert.Equal(3f, lateral, 3);   // +x is to the right when driving along +z
    }

    [Fact]
    public void Route_frame_ignores_height_and_a_zero_direction()
    {
        var (along, lateral) = RouteFrame(new Vector3(-2f, 40f, 0f), Vector3.zero, new Vector3(0f, 0f, 2f));
        Assert.Equal(0f, along, 3);
        Assert.Equal(-2f, lateral, 3);
        Assert.Equal((0f, 0f), RouteFrame(Vector3.one, Vector3.zero, Vector3.up));
    }

    [Fact]
    public void A_stretch_covers_its_distance_range_across_its_width()
    {
        var ice = Stretch(SurfaceKind.Ice, 100f, 140f, 7f);
        Assert.True(Contains(ice, 120f, 0f));
        Assert.True(Contains(ice, 100f, -7f));
        Assert.False(Contains(ice, 99f, 0f));
        Assert.False(Contains(ice, 120f, 7.5f));
    }

    [Fact]
    public void A_puddle_is_an_oval_not_its_bounding_box()
    {
        var puddle = Puddle(200f, 210f, 2f, 2f);   // centre 205, radii 5 along and 2 across
        Assert.True(Contains(puddle, 205f, 2f));
        Assert.True(Contains(puddle, 209.9f, 2f));
        Assert.False(Contains(puddle, 209f, 3.8f));    // inside the box, outside the oval
        Assert.False(Contains(puddle, 205f, -0.5f));
    }

    [Fact]
    public void Snow_packs_once_per_car_down_to_a_floor()
    {
        var snow = Stretch(SurfaceKind.Snow, 0f, 50f);
        Assert.True(NotePass(snow, "leader", 0.5f, 0.2f));
        Assert.Equal(0.5f, snow.Strength, 3);
        Assert.False(NotePass(snow, "leader", 0.5f, 0.2f));   // the same car again: no change
        Assert.Equal(0.5f, snow.Strength, 3);
        NotePass(snow, "second", 0.5f, 0.2f);
        NotePass(snow, "third", 0.5f, 0.2f);
        Assert.Equal(0.2f, snow.Strength, 3);
    }

    [Fact]
    public void Other_surfaces_do_not_pack()
    {
        var mud = Stretch(SurfaceKind.Mud, 0f, 50f);
        Assert.True(NotePass(mud, "leader", 0.5f, 0.2f));
        Assert.Equal(1f, mud.Strength);
    }

    [Fact]
    public void Mud_drags_harder_the_slower_the_car_already_is()
    {
        float fast = BogDrag(40f, 1f, 0.3f, 2f, 15f);
        float slow = BogDrag(5f, 1f, 0.3f, 2f, 15f);
        float stopped = BogDrag(0f, 1f, 0.3f, 2f, 15f);
        Assert.Equal(0.3f, fast, 3);
        Assert.True(slow > fast);
        Assert.Equal(2.3f, stopped, 3);
        Assert.Equal(stopped * 0.5f, BogDrag(0f, 0.5f, 0.3f, 2f, 15f), 3);   // packed snow drags less
    }

    [Fact]
    public void Overlapping_effects_multiply_speed_and_grip_and_add_drag()
    {
        var mud = new SurfaceEffect
        {
            Speed = 0.5f,
            Grip = 1f,
            Drag = 0.4f,
        };
        var ice = new SurfaceEffect
        {
            Speed = 1f,
            Grip = 0.35f,
            Drag = 0.1f,
        };
        var both = Combine(Combine(SurfaceEffect.None, mud), ice);
        Assert.Equal(0.5f, both.Speed, 3);
        Assert.Equal(0.35f, both.Grip, 3);
        Assert.Equal(0.5f, both.Drag, 3);
        Assert.True(SurfaceEffect.None.IsNone);
        Assert.False(both.IsNone);
    }

    [Fact]
    public void Stretches_stay_inside_the_range_and_apart()
    {
        var random = new System.Random(7);
        var placed = PlaceStretches(random, SurfaceKind.Mud, 5, 30f, 50f, 1000f, 7f, null, null, gap: 10f);
        Assert.Equal(5, placed.Count);
        Assert.All(placed, patch => Assert.InRange(patch.Start, 50f, 970f));
        Assert.All(placed, patch => Assert.Equal(30f, patch.End - patch.Start, 3));
        var ordered = placed.OrderBy(patch => patch.Start).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i].Start >= ordered[i - 1].End + 10f);
        }
    }

    [Fact]
    public void Stretches_avoid_patches_already_on_the_road_and_give_up_when_full()
    {
        var random = new System.Random(3);
        var existing = new[] { Stretch(SurfaceKind.Snow, 0f, 90f) };
        var placed = PlaceStretches(random, SurfaceKind.Snow, 10, 40f, 0f, 200f, 7f, null, existing, gap: 10f);
        Assert.InRange(placed.Count, 1, 2);   // only 100..200 is free: room for two at most
        Assert.All(placed, patch => Assert.True(patch.Start >= 100f));
    }

    [Fact]
    public void Ice_weighted_by_turning_lands_on_the_corner()
    {
        var random = new System.Random(11);
        float CornerWeight(float start) => start > 380f && start < 420f ? 1f : 0f;
        var placed = PlaceStretches(random, SurfaceKind.Ice, 1, 40f, 0f, 1000f, 7f, CornerWeight, null);
        Assert.Single(placed);
        Assert.InRange(placed[0].Start, 380f, 420f);
    }

    [Fact]
    public void Puddles_sit_fully_on_the_road_and_never_overlap()
    {
        var random = new System.Random(5);
        var placed = PlacePuddles(random, 12, 0f, 600f, 6f, 1.5f, 3f, null);
        Assert.Equal(12, placed.Count);
        foreach (var puddle in placed)
        {
            Assert.True(Mathf.Abs(puddle.Lateral) + puddle.HalfWidth <= 6f + 1e-3f);
            Assert.InRange(puddle.Start, 0f, 600f);
            Assert.InRange(puddle.End, 0f, 600f);
        }
        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = i + 1; j < placed.Count; j++)
            {
                bool alongOverlap = placed[i].Start < placed[j].End && placed[j].Start < placed[i].End;
                float sideGap = Mathf.Abs(placed[i].Lateral - placed[j].Lateral);
                bool sideOverlap = sideGap < placed[i].HalfWidth + placed[j].HalfWidth;
                Assert.False(alongOverlap && sideOverlap);
            }
        }
    }
}
