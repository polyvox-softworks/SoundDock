using System.Drawing;

namespace SoundDock.UI;

/// <summary>
/// Pure taskbar-widget geometry. Inputs and results use screen pixels, including
/// negative monitor origins. No Explorer window is moved or resized here.
/// </summary>
internal static class TaskbarPlacement
{
    internal static Rectangle Calculate(Rectangle taskbar, Rectangle notification,
        IReadOnlyList<Rectangle> obstacles, Size widgetSize, int gap, int extraOffset)
    {
        if (taskbar.Width <= 0 || taskbar.Height <= 0 ||
            widgetSize.Width <= 0 || widgetSize.Height <= 0)
            return Rectangle.Empty;

        bool horizontal = taskbar.Width >= taskbar.Height;
        long spacing = Math.Max(0, gap);
        long offset = Math.Max(0, extraOffset);
        int length = horizontal ? widgetSize.Width : widgetSize.Height;
        int crossLength = horizontal ? widgetSize.Height : widgetSize.Width;
        int availableCrossLength = horizontal ? taskbar.Height : taskbar.Width;
        if (crossLength > availableCrossLength) return Rectangle.Empty;

        // The gap applies along the taskbar, not across its thickness. A widget
        // may legitimately occupy the taskbar's full height (or vertical width).
        long first = (horizontal ? taskbar.X : taskbar.Y) + spacing;
        long last = (horizontal ? (long)taskbar.X + taskbar.Width : (long)taskbar.Y + taskbar.Height)
                    - spacing - length;
        if (last < first) return Rectangle.Empty;

        long crossPosition = (horizontal ? taskbar.Y : taskbar.X)
                             + ((long)availableCrossLength - crossLength) / 2;
        long anchor = (horizontal ? notification.X : notification.Y) - spacing - length;
        long? position = ClearObstacles(Math.Clamp(anchor, first, last));
        if (position is null) return Rectangle.Empty;

        // Offsets are deliberately applied after automatic collision avoidance.
        // Moving farther left/up can encounter another widget, so validate again.
        position = ClearObstacles(Math.Clamp(position.Value - offset, first, last));
        if (position is null) return Rectangle.Empty;

        long x = horizontal ? position.Value : crossPosition;
        long y = horizontal ? crossPosition : position.Value;
        if (x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue)
            return Rectangle.Empty;
        return new Rectangle((int)x, (int)y, widgetSize.Width, widgetSize.Height);

        long? ClearObstacles(long candidate)
        {
            // Every successful move passes at least one obstacle's near edge.
            // The extra pass handles a collision at the clamped taskbar boundary.
            for (int pass = 0; pass <= obstacles.Count + 1; pass++)
            {
                long next = candidate;
                bool collision = false;
                Consider(notification);
                foreach (var obstacle in obstacles) Consider(obstacle);
                if (!collision) return candidate;

                next = Math.Max(first, next);
                if (next >= candidate) return null;
                candidate = next;

                void Consider(Rectangle obstacle)
                {
                    if (obstacle.Width <= 0 || obstacle.Height <= 0) return;
                    long candidateX = horizontal ? candidate : crossPosition;
                    long candidateY = horizontal ? crossPosition : candidate;
                    bool intersects = candidateX < (long)obstacle.X + obstacle.Width &&
                                      candidateX + widgetSize.Width > obstacle.X &&
                                      candidateY < (long)obstacle.Y + obstacle.Height &&
                                      candidateY + widgetSize.Height > obstacle.Y;
                    if (!intersects) return;
                    collision = true;
                    long obstacleEdge = horizontal ? obstacle.X : obstacle.Y;
                    next = Math.Min(next, obstacleEdge - spacing - length);
                }
            }
            return null;
        }
    }
}
