using PCL.UI.Icons;

namespace PCL.Extensions.ScreenshotManager;

internal static class ScreenshotIcons
{
    // A 24x24 camera: top bump, body frame and a filled lens.
    public static readonly PclIcon Camera = new(
        "M9 4 H15 V7 H9 Z " +
        "M3 7 H21 V9 H3 Z " +
        "M3 18 H21 V20 H3 Z " +
        "M3 9 H5 V18 H3 Z " +
        "M19 9 H21 V18 H19 Z " +
        "M12 9.8 A3.2 3.2 0 1 0 12 16.2 A3.2 3.2 0 1 0 12 9.8 Z");
}