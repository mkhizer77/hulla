using System;
using System.Collections.Generic;
using Hulla.Application.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// The small life in a theme's scene (docs/design/table-themes.md §3): string lights that glow
    /// unevenly and now and then dip, lanterns that breathe and sway, fairy lights that twinkle,
    /// garlands that sway, a petal that falls, motes in a spotlight, steam off a chai glass. Every
    /// effect is slow and small so it reads as atmosphere, never as an event. Sits behind a
    /// screen's content (or on the table, for the glasses), never takes a touch, and stands still
    /// under reduce motion. Deterministic for a seed, so snapshots are stable.
    /// </summary>
    public sealed class AmbientLayer : VisualElement
    {
        public const string ScreenName = "ambient";
        public const string TableName = "ambient-table";

        private const float StepSeconds = 1f / 30f;

        private readonly List<Mover> _movers = new List<Mover>();
        private readonly System.Random _rng;
        private float _time;

        public AmbientLayer(TableSkin skin, bool reduceMotion, bool onTable = false, int seed = 17)
        {
            name = onTable ? TableName : ScreenName;
            AddToClassList("ambient");
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            _rng = new System.Random(seed + (int)skin * 101 + (onTable ? 7 : 0));
            Skin = skin;
            if (onTable)
            {
                BuildTable(skin);
            }
            else
            {
                BuildScreen(skin);
            }

            Advance(0f);
            if (!reduceMotion)
            {
                schedule.Execute(state => Advance(Math.Min(0.1f, state.deltaTime / 1000f))).Every((long)(StepSeconds * 1000f));
            }
        }

        public TableSkin Skin { get; }

        /// <summary>How many things move; zero for a theme with nothing to animate.</summary>
        public int MoverCount => _movers.Count;

        /// <summary>
        /// Puts the theme's layer behind <paramref name="screenRoot"/>'s content, and the table's
        /// glasses on its rail when it has one; replaces any layer already there.
        /// </summary>
        public static void Decorate(VisualElement? screenRoot, TableSkin skin, bool reduceMotion)
        {
            if (screenRoot == null)
            {
                return;
            }

            screenRoot.Q(ScreenName)?.RemoveFromHierarchy();
            screenRoot.Q(TableName)?.RemoveFromHierarchy();
            screenRoot.Insert(0, new AmbientLayer(skin, reduceMotion));
            var rail = screenRoot.Q("rail");
            if (rail != null && (skin == TableSkin.Adda || skin == TableSkin.Dhaba))
            {
                rail.Add(new AmbientLayer(skin, reduceMotion, onTable: true));
            }
        }

        /// <summary>Moves time on by <paramref name="seconds"/>; the scheduler calls it, tests may too.</summary>
        public void Advance(float seconds)
        {
            _time += seconds;
            foreach (var mover in _movers)
            {
                mover.Update(_time, seconds);
            }
        }

        private void BuildScreen(TableSkin skin)
        {
            switch (skin)
            {
                case TableSkin.Club:
                    var cone = Sprite("ambient__cone", 15f, -4f, null, null);
                    cone.style.width = Length.Percent(70f);
                    cone.style.height = Length.Percent(64f);
                    cone.style.unityBackgroundImageTintColor = new Color(0.8f, 0.86f, 1f);
                    _movers.Add(new Breathe(cone, 0.22f, 0.04f, Range(7f, 9f), Range(0f, 6f)));
                    Motes(7, new Rect(30f, 6f, 40f, 48f), new Color(0.86f, 0.9f, 1f));
                    break;
                case TableSkin.Haveli:
                    Lantern(3.5f, 0.95f, 6.2f);
                    Lantern(96.5f, -0.85f, 7.1f);
                    Motes(6, new Rect(4f, 4f, 92f, 30f), new Color(1f, 0.8f, 0.55f));
                    break;
                case TableSkin.Dhaba:
                    for (var i = 0; i < 10; i++)
                    {
                        var star = Sprite("ambient__glow", Range(3f, 97f), Range(1.5f, 30f), 9f, 9f);
                        star.style.unityBackgroundImageTintColor = Color.white;
                        _movers.Add(new Breathe(star, 0.4f, 0.32f, Range(3f, 7f), Range(0f, 7f)));
                    }

                    StringLights(new Vector2(-2f, 9f), new Vector2(33f, 2f), 4.5f, 6, 1.15f, WarmBulbs);
                    StringLights(new Vector2(33f, 2f), new Vector2(67f, 2f), 3.5f, 6, 1.15f, WarmBulbs);
                    StringLights(new Vector2(67f, 2f), new Vector2(102f, 9f), 4.5f, 6, 1.15f, WarmBulbs);
                    break;
                case TableSkin.Mehndi:
                    StringLights(new Vector2(-2f, 1f), new Vector2(102f, 1.5f), 3.5f, 16, 0.55f, new[] { new Color(1f, 0.95f, 0.7f) });
                    Garland(1.6f, 440f, 1.3f, 5.6f);
                    Garland(5.4f, 290f, -1.1f, 6.4f);
                    Garland(98.4f, 440f, -1.2f, 6.0f);
                    Garland(94.6f, 290f, 1.0f, 7.0f);
                    for (var i = 0; i < 4; i++)
                    {
                        var petal = Sprite("ambient__petal", Range(10f, 90f), -6f, 24f, 24f);
                        petal.style.unityBackgroundImageTintColor = i % 3 == 0 ? new Color(0.91f, 0.12f, 0.39f) : i % 3 == 1 ? new Color(1f, 0.7f, 0f) : new Color(0.9f, 0.32f, 0f);
                        _movers.Add(new Fall(petal, Range(10f, 90f), Range(11f, 15f), Range(0f, 12f), _rng.Next()));
                    }

                    break;
                case TableSkin.Adda:
                    var warm = Sprite("ambient__glow", 50f, 0f, 1100f, 1100f);
                    warm.style.translate = new Translate(-550f, -520f);
                    warm.style.unityBackgroundImageTintColor = new Color(1f, 0.78f, 0.47f);
                    _movers.Add(new Breathe(warm, 0.3f, 0.05f, Range(8f, 10f), 0f));
                    Motes(6, new Rect(8f, 4f, 84f, 36f), new Color(1f, 0.85f, 0.6f));
                    break;
            }
        }

        /// <summary>Chai glasses on the table with steam coming off them.</summary>
        private void BuildTable(TableSkin skin)
        {
            var spots = skin == TableSkin.Adda
                ? new[] { new Vector2(9f, 80f), new Vector2(85f, 12f) }
                : new[] { new Vector2(8f, 9f), new Vector2(86f, 84f) };
            foreach (var spot in spots)
            {
                for (var k = 0; k < 2; k++)
                {
                    var wisp = Sprite("ambient__steam", spot.x, spot.y, 52f, 120f);
                    wisp.style.translate = new Translate(-4f, -118f);
                    _movers.Add(new Rise(wisp, Range(3.4f, 4.2f), k * 1.9f + Range(0f, 0.6f)));
                }

                var glass = Sprite("ambient__glass", spot.x, spot.y, 44f, 58f);
                glass.style.translate = new Translate(0f, -10f);
            }
        }

        /// <summary>The Rooftop's festoon: warm white and amber, like the concept art.</summary>
        private static readonly Color[] WarmBulbs =
        {
            new Color(1f, 0.93f, 0.7f),
            new Color(1f, 0.85f, 0.5f),
        };

        private static readonly Color[] Bulbs =
        {
            new Color(1f, 0.82f, 0.25f),
            new Color(1f, 0.31f, 0.6f),
            new Color(0.18f, 0.9f, 0.63f),
            new Color(1f, 0.95f, 0.75f),
        };

        /// <summary>A sagging wire between two points (percent of the layer) with lights hung along it.</summary>
        private void StringLights(Vector2 from, Vector2 to, float sag, int count, float scale, Color[] colours)
        {
            var wire = new VisualElement { pickingMode = PickingMode.Ignore };
            wire.style.position = Position.Absolute;
            wire.style.left = 0;
            wire.style.top = 0;
            wire.style.right = 0;
            wire.style.bottom = 0;
            wire.generateVisualContent += context =>
            {
                var rect = wire.contentRect;
                var painter = context.painter2D;
                painter.strokeColor = new Color(0.08f, 0.06f, 0.06f, 0.85f);
                painter.lineWidth = 2.5f;
                painter.BeginPath();
                for (var i = 0; i <= 40; i++)
                {
                    var at = Along(from, to, sag, i / 40f);
                    var point = new Vector2(at.x / 100f * rect.width, at.y / 100f * rect.height);
                    if (i == 0)
                    {
                        painter.MoveTo(point);
                    }
                    else
                    {
                        painter.LineTo(point);
                    }
                }

                painter.Stroke();
            };
            Add(wire);
            for (var i = 0; i < count; i++)
            {
                var at = Along(from, to, sag, (i + 0.5f) / count);
                var colour = colours[i % colours.Length];
                var halo = Sprite("ambient__glow", at.x, at.y, 96f * scale, 96f * scale);
                halo.style.translate = new Translate(-48f * scale, -36f * scale);
                halo.style.unityBackgroundImageTintColor = colour;
                var bulb = Sprite("ambient__bulb", at.x, at.y, 26f * scale, 26f * scale);
                bulb.style.translate = new Translate(-13f * scale, -2f * scale);
                bulb.style.unityBackgroundImageTintColor = Color.Lerp(colour, Color.white, 0.45f);
                _movers.Add(new Flicker(halo, bulb, 0.72f, 0.07f, Range(2.5f, 5.5f), Range(0f, 6f), Range(4f, 14f), _rng.Next()));
            }
        }

        private static Vector2 Along(Vector2 from, Vector2 to, float sag, float t) =>
            new Vector2(Mathf.Lerp(from.x, to.x, t), Mathf.Lerp(from.y, to.y, t) + sag * 4f * t * (1f - t));

        private void Lantern(float x, float swing, float period)
        {
            var lamp = Sprite("ambient__lamp", x, 0f, 78f, 143f);
            lamp.style.translate = new Translate(-39f, -10f);
            lamp.style.transformOrigin = new TransformOrigin(Length.Percent(50f), 0f);
            var halo = new VisualElement { pickingMode = PickingMode.Ignore };
            halo.AddToClassList("ambient__glow");
            halo.style.position = Position.Absolute;
            halo.style.left = -171f;
            halo.style.top = -120f;
            halo.style.width = 420f;
            halo.style.height = 420f;
            halo.style.unityBackgroundImageTintColor = new Color(1f, 0.69f, 0.31f);
            lamp.Insert(0, halo);
            _movers.Add(new Sway(lamp, swing, period, Range(0f, 6f)));
            _movers.Add(new Flame(halo, 0.5f, Range(0f, 6f)));
        }

        private void Garland(float x, float length, float swing, float period)
        {
            var strand = Sprite("ambient__marigolds", x, 0f, 36f, length);
            strand.style.translate = new Translate(-18f, -8f);
            strand.style.transformOrigin = new TransformOrigin(Length.Percent(50f), 0f);
            _movers.Add(new Sway(strand, swing, period, Range(0f, 6f)));
        }

        private void Motes(int count, Rect area, Color tint)
        {
            for (var i = 0; i < count; i++)
            {
                var size = Range(8f, 14f);
                var mote = Sprite("ambient__mote", Range(area.xMin, area.xMax), Range(area.yMin, area.yMax), size, size);
                mote.style.unityBackgroundImageTintColor = tint;
                _movers.Add(new Drift(mote, Range(18f, 40f), Range(9f, 16f), Range(0f, 9f)));
            }
        }

        /// <summary>An absolutely placed sprite at a percent position with a pixel size (null keeps USS's).</summary>
        private VisualElement Sprite(string cls, float xPercent, float yPercent, float? width, float? height)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(cls);
            element.style.position = Position.Absolute;
            element.style.left = Length.Percent(xPercent);
            element.style.top = Length.Percent(yPercent);
            if (width != null)
            {
                element.style.width = width.Value;
            }

            if (height != null)
            {
                element.style.height = height.Value;
            }

            Add(element);
            return element;
        }

        private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        private abstract class Mover
        {
            public abstract void Update(float time, float dt);

            protected static float Wave(float time, float period, float phase) => Mathf.Sin((time / period + phase) * Mathf.PI * 2f);
        }

        /// <summary>Opacity rising and falling slowly.</summary>
        private sealed class Breathe : Mover
        {
            private readonly VisualElement _element;
            private readonly float _base;
            private readonly float _amp;
            private readonly float _period;
            private readonly float _phase;

            public Breathe(VisualElement element, float baseOpacity, float amp, float period, float phase)
            {
                _element = element;
                _base = baseOpacity;
                _amp = amp;
                _period = period;
                _phase = phase;
            }

            public override void Update(float time, float dt) =>
                _element.style.opacity = Mathf.Clamp01(_base + _amp * Wave(time, _period, _phase));
        }

        /// <summary>A lantern's light: two waves at different speeds, so it never settles into a beat.</summary>
        private sealed class Flame : Mover
        {
            private readonly VisualElement _element;
            private readonly float _base;
            private readonly float _phase;

            public Flame(VisualElement element, float baseOpacity, float phase)
            {
                _element = element;
                _base = baseOpacity;
                _phase = phase;
            }

            public override void Update(float time, float dt)
            {
                var v = _base + 0.07f * Wave(time, 2.3f, _phase) + 0.035f * Wave(time, 0.41f, _phase * 1.7f);
                _element.style.opacity = Mathf.Clamp01(v);
                var s = 1f + 0.02f * Wave(time, 3.1f, _phase);
                _element.style.scale = new Scale(new Vector2(s, s));
            }
        }

        /// <summary>
        /// A string bulb: a slow uneven glow, and every so often a short dip, sometimes twice, the way
        /// a loose connection does on a rooftop.
        /// </summary>
        private sealed class Flicker : Mover
        {
            private readonly VisualElement _halo;
            private readonly VisualElement _bulb;
            private readonly float _base;
            private readonly float _amp;
            private readonly float _period;
            private readonly float _phase;
            private readonly System.Random _rng;
            private float _nextDip;
            private float _dipLeft;
            private int _dipsQueued;

            public Flicker(VisualElement halo, VisualElement bulb, float baseOpacity, float amp, float period, float phase, float firstDip, int seed)
            {
                _halo = halo;
                _bulb = bulb;
                _base = baseOpacity;
                _amp = amp;
                _period = period;
                _phase = phase;
                _nextDip = firstDip;
                _rng = new System.Random(seed);
            }

            public override void Update(float time, float dt)
            {
                if (_dipLeft > 0f)
                {
                    _dipLeft -= dt;
                }
                else if (time >= _nextDip && dt > 0f)
                {
                    _dipLeft = 0.08f + (float)_rng.NextDouble() * 0.08f;
                    if (_dipsQueued > 0)
                    {
                        _dipsQueued--;
                        _nextDip = time + 0.18f;
                    }
                    else
                    {
                        _dipsQueued = _rng.NextDouble() < 0.35 ? 1 : 0;
                        _nextDip = time + (_dipsQueued > 0 ? 0.18f : 6f + (float)_rng.NextDouble() * 10f);
                    }
                }

                var glow = _base + _amp * Wave(time, _period, _phase);
                var dip = _dipLeft > 0f ? 0.45f : 1f;
                _halo.style.opacity = Mathf.Clamp01(glow * dip);
                _bulb.style.opacity = Mathf.Clamp01((0.85f + 0.1f * Wave(time, _period, _phase)) * (_dipLeft > 0f ? 0.7f : 1f));
            }
        }

        /// <summary>A hanging thing swinging a degree or so about its top.</summary>
        private sealed class Sway : Mover
        {
            private readonly VisualElement _element;
            private readonly float _degrees;
            private readonly float _period;
            private readonly float _phase;

            public Sway(VisualElement element, float degrees, float period, float phase)
            {
                _element = element;
                _degrees = degrees;
                _period = period;
                _phase = phase;
            }

            public override void Update(float time, float dt) =>
                _element.style.rotate = new Rotate(new Angle(_degrees * Wave(time, _period, _phase)));
        }

        /// <summary>A mote of dust wandering on a slow loop, fading in and out.</summary>
        private sealed class Drift : Mover
        {
            private readonly VisualElement _element;
            private readonly float _radius;
            private readonly float _period;
            private readonly float _phase;

            public Drift(VisualElement element, float radius, float period, float phase)
            {
                _element = element;
                _radius = radius;
                _period = period;
                _phase = phase;
            }

            public override void Update(float time, float dt)
            {
                var x = _radius * Wave(time, _period, _phase);
                var y = _radius * 0.6f * Wave(time, _period * 1.37f, _phase + 0.25f);
                _element.style.translate = new Translate(x, y);
                _element.style.opacity = Mathf.Clamp01(0.32f + 0.26f * Wave(time, _period * 0.61f, _phase));
            }
        }

        /// <summary>A petal drifting down from above the screen, turning as it goes, then waiting its turn again.</summary>
        private sealed class Fall : Mover
        {
            private readonly VisualElement _element;
            private readonly float _duration;
            private readonly float _delay;
            private readonly System.Random _rng;
            private float _x;
            private float _start;

            public Fall(VisualElement element, float x, float duration, float delay, int seed)
            {
                _element = element;
                _x = x;
                _duration = duration;
                _delay = delay;
                _start = delay;
                _rng = new System.Random(seed);
            }

            public override void Update(float time, float dt)
            {
                var p = (time - _start) / _duration;
                if (p > 1f)
                {
                    _start = time + 2f + (float)_rng.NextDouble() * 8f;
                    _x = 10f + (float)_rng.NextDouble() * 80f;
                    p = -1f;
                }

                if (p < 0f)
                {
                    _element.style.opacity = 0f;
                    return;
                }

                _element.style.left = Length.Percent(_x + 3f * Mathf.Sin(p * Mathf.PI * 3f));
                _element.style.top = Length.Percent(-4f + p * 108f);
                _element.style.rotate = new Rotate(new Angle(p * 400f));
                _element.style.opacity = 0.8f * Mathf.Clamp01(Mathf.Min(p * 8f, (1f - p) * 6f));
            }
        }

        /// <summary>A wisp of steam rising, widening and thinning out, then starting again.</summary>
        private sealed class Rise : Mover
        {
            private readonly VisualElement _element;
            private readonly float _duration;
            private readonly float _offset;

            public Rise(VisualElement element, float duration, float offset)
            {
                _element = element;
                _duration = duration;
                _offset = offset;
            }

            public override void Update(float time, float dt)
            {
                var p = Mathf.Repeat(time + _offset, _duration) / _duration;
                _element.style.opacity = 0.5f * Mathf.Sin(p * Mathf.PI);
                var s = 0.75f + 0.45f * p;
                _element.style.scale = new Scale(new Vector2(s, s));
                _element.style.translate = new Translate(-4f + 5f * Mathf.Sin(p * Mathf.PI * 2f), -118f - 46f * p);
            }
        }
    }
}
