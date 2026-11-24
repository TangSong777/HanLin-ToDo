using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Forms = System.Windows.Forms;

namespace HanLin.ToDo;

internal static class DisplayLayout
{
    // Identifies the current monitor arrangement so each layout keeps its own note placement.
    public static string Signature()
    {
        var screens = Forms.Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .Select(screen => string.Join(",",
                screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height,
                screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height,
                screen.Primary ? 1 : 0));
        string layout = string.Join("|", screens);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(layout)))[..16];
    }
}
