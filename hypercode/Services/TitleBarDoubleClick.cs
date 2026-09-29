using System.Runtime.InteropServices;

namespace Hypercode.Services;

/// <summary>O que o duplo-clique na barra de título faz, conforme Ajustes do Sistema.</summary>
public enum TitleBarDoubleClickAction
{
    /// <summary>Zoom (o padrão do macOS): alterna entre o tamanho atual e o que cabe o conteúdo.</summary>
    Zoom,
    Minimize,
    None,
}

/// <summary>
/// Lê a preferência "Clicar duas vezes na barra de título de uma janela para" (Ajustes do
/// Sistema → Área de Trabalho e Dock), a AppleActionOnDoubleClick do domínio global. A faixa de
/// abas ocupa a barra de título (#91), então o duplo-clique nela tem de fazer o mesmo que o
/// sistema faz numa barra de título de verdade.
/// </summary>
public static class TitleBarDoubleClick
{
    public static TitleBarDoubleClickAction Current()
        => OperatingSystem.IsMacOS() ? Parse(ReadGlobalString("AppleActionOnDoubleClick")) : TitleBarDoubleClickAction.Zoom;

    /// <summary>
    /// "Minimize" minimiza e "None" não faz nada. "Maximize" (zoom) e "Fill" (preencher, do macOS
    /// 15 em diante) viram zoom; sem a chave gravada, também — é o padrão do sistema.
    /// </summary>
    public static TitleBarDoubleClickAction Parse(string? value) => value switch
    {
        "Minimize" => TitleBarDoubleClickAction.Minimize,
        "None" => TitleBarDoubleClickAction.None,
        _ => TitleBarDoubleClickAction.Zoom,
    };

    private static string? ReadGlobalString(string key)
    {
        var cfKey = CFStringCreateWithCString(IntPtr.Zero, key, KCFStringEncodingUtf8);
        // kCFPreferencesAnyApplication: o valor da constante é o próprio nome.
        var anyApp = CFStringCreateWithCString(IntPtr.Zero, "kCFPreferencesAnyApplication", KCFStringEncodingUtf8);
        try
        {
            var value = CFPreferencesCopyAppValue(cfKey, anyApp);
            if (value == IntPtr.Zero) return null;
            try
            {
                if (CFGetTypeID(value) != CFStringGetTypeID()) return null;
                var buffer = new byte[256];
                return CFStringGetCString(value, buffer, buffer.Length, KCFStringEncodingUtf8)
                    ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))
                    : null;
            }
            finally { CFRelease(value); }
        }
        finally
        {
            CFRelease(cfKey);
            CFRelease(anyApp);
        }
    }

    private const uint KCFStringEncodingUtf8 = 0x08000100;
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);
    [DllImport(CoreFoundation)] private static extern nuint CFGetTypeID(IntPtr value);
    [DllImport(CoreFoundation)] private static extern nuint CFStringGetTypeID();
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
