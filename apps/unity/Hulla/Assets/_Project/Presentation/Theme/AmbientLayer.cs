using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// The small life in the rooftop scene (docs/design/rooftop-redesign.md): stars that twinkle over
    /// the backdrop, whose own string lights are painted in, and steam off the chai glasses on the
    /// table's rail. Every effect is slow and small so it reads as atmosphere, never as an event. Sits
    /// behind a screen's content (or on the rail, for the glasses), never takes a touch, and stands
    /// still under reduce motion. Deterministic for a seed, so snapshots are stable.
    /// </summary>
    public sealed class AmbientLayer : VisualElement
    {
        public const string ScreenName = "ambient";
        public const string TableName = "ambient-table";

        private const float StepSeconds = 1f / 30f;

        private readonly List<Mover> _movers = new List<Mover>();
        private readonly System.Random _rng;
        private float _time;

        public AmbientLayer(bool reduceMotion, bool onTable = false, int seed = 17)
        {
            name = onTable ? TableName : ScreenName;
            AddToClassList("ambient");
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            _rng = new System.Random(seed + (onTable ? 7 : 0));
            if (onTable)
            {
                BuildTable();
            }
            else
            {
                BuildScreen();
            }

            Advance(0f);
            if (!reduceMotion)
            {
                schedule.Execute(state => Advance(Math.Min(0.1f, state.deltaTime / 1000f))).Every((long)(StepSeconds * 1000f));
            }
        }

        /// <summary>How many things move.</summary>
        public int MoverCount => _movers.Count;

        /// <summary>
        /// Puts the layer behind <paramref name="screenRoot"/>'s content, and the glasses on the table's
        /// rail when it has one; replaces any layer already there.
        /// </summary>
        public static void Decorate(VisualElement? screenRoot, bool reduceMotion)
        {
            if (screenRoot == null)
            {
                return;
            }

            screenRoot.Q(ScreenName)?.RemoveFromHierarchy();
            screenRoot.Q(TableName)?.RemoveFromHierarchy();
            screenRoot.Insert(0, new AmbientLayer(reduceMotion));
            var rail = screenRoot.Q("rail");
            rail?.Add(new AmbientLayer(reduceMotion, onTable: true));
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

        /// <summary>A few stars in the upper sky brighten and fade, each at its own pace.</summary>
        private void BuildScreen()
        {
            for (var i = 0; i < 10; i++)
            {
                var star = Sprite("ambient__glow", Range(3f, 97f), Range(8f, 30f), 9f, 9f);
                star.style.unityBackgroundImageTintColor = Color.white;
                _movers.Add(new Breathe(star, 0.4f, 0.32f, Range(3f, 7f), Range(0f, 7f)));
            }
        }

        /// <summary>Chai glasses on the table with steam coming off them.</summary>
        private void BuildTable()
        {
            foreach (var spot in new[] { new Vector2(8f, 9f), new Vector2(86f, 84f) })
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

        /// <summary>An absolutely placed sprite at a percent position with a pixel size.</summary>
        private VisualElement Sprite(string cls, float xPercent, float yPercent, float width, float height)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(cls);
            element.style.position = Position.Absolute;
            element.style.left = Length.Percent(xPercent);
            element.style.top = Length.Percent(yPercent);
            element.style.width = width;
            element.style.height = height;
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
