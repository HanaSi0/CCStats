using System;
using System.Numerics;

namespace PvpStats.Windows.Floating;

/// <summary>Pointer interaction in viewport-relative logical units; independent of ImGui/native code.</summary>
public sealed class LauncherInteraction {
    public static readonly Vector2 DefaultPosition = new(40, 200);
    public bool IsDragging { get; private set; }
    private Vector2 _startPointer;
    private Vector2 _startPosition;

    public readonly record struct Result(Vector2 Position, bool OpenWindow, bool SavePosition);

    public Result Update(Vector2 position, Vector2 pointer, Vector2 viewportSize, float size,
        bool leftPressed, bool rightPressed, bool rightDown) {
        var bounded = Clamp(position, viewportSize, size);
        var wasDragging = IsDragging;
        var save = !IsDragging && bounded != position;
        if(rightPressed && !IsDragging) {
            IsDragging = true;
            _startPointer = pointer;
            _startPosition = bounded;
        }
        if(IsDragging) {
            bounded = Clamp(_startPosition + pointer - _startPointer, viewportSize, size);
            if(!rightDown) {
                IsDragging = false;
                save = bounded != _startPosition;
            }
        }
        var open = leftPressed && !rightPressed && !rightDown && !wasDragging && !IsDragging;
        return new(bounded, open, save);
    }

    public void CancelDrag() => IsDragging = false;

    public static Vector2 Clamp(Vector2 position, Vector2 viewportSize, float size) {
        var x = float.IsFinite(position.X) ? position.X : DefaultPosition.X;
        var y = float.IsFinite(position.Y) ? position.Y : DefaultPosition.Y;
        return new(Math.Clamp(x, 0, Math.Max(0, viewportSize.X - size)),
            Math.Clamp(y, 0, Math.Max(0, viewportSize.Y - size)));
    }
}
