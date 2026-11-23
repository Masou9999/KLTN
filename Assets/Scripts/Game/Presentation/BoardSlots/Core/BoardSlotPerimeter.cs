using System;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Walks the outline of a rectangle centred on the origin (UI space, +y up),
    /// starting at the top-left corner and moving clockwise: top edge, right edge,
    /// bottom edge, left edge.
    /// </summary>
    public static class BoardSlotPerimeter
    {
        #region Public API

        public static float Length(float width, float height)
        {
            return 2f * (Math.Max(0f, width) + Math.Max(0f, height));
        }

        /// <summary>
        /// Returns the point at <paramref name="distance"/> along the outline. The
        /// distance wraps, so negative values and values past the length are valid.
        /// </summary>
        public static void Evaluate(
            float width,
            float height,
            float distance,
            out float x,
            out float y
        )
        {
            width = Math.Max(0f, width);
            height = Math.Max(0f, height);
            float length = Length(width, height);

            if (length <= 0f)
            {
                x = 0f;
                y = 0f;
                return;
            }

            float d = Wrap(distance, length);
            float left = -width * 0.5f;
            float right = width * 0.5f;
            float top = height * 0.5f;
            float bottom = -height * 0.5f;

            if (d < width)
            {
                x = left + d;
                y = top;
                return;
            }

            d -= width;

            if (d < height)
            {
                x = right;
                y = top - d;
                return;
            }

            d -= height;

            if (d < width)
            {
                x = right - d;
                y = bottom;
                return;
            }

            d -= width;
            x = left;
            y = bottom + d;
        }

        /// <summary>
        /// Distances of the four corners measured from the start (top-left), in
        /// walking order: top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public static float CornerDistance(float width, float height, int cornerIndex)
        {
            width = Math.Max(0f, width);
            height = Math.Max(0f, height);

            switch (((cornerIndex % 4) + 4) % 4)
            {
                case 0:
                    return 0f;
                case 1:
                    return width;
                case 2:
                    return width + height;
                default:
                    return 2f * width + height;
            }
        }

        public static float Wrap(float distance, float length)
        {
            if (length <= 0f)
            {
                return 0f;
            }

            float wrapped = distance % length;
            return wrapped < 0f ? wrapped + length : wrapped;
        }

        #endregion
    }
}
