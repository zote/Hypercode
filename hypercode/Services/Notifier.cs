namespace Hypercode.Services;

/// <summary>
/// Notificação da Central de Notificações via `display notification` do AppleScript. Sem
/// assinatura nem entitlement, o macOS atribui a notificação ao "Editor de Script" — é o
/// preço de não depender de API nativa; o texto diz de onde veio.
/// </summary>
public static class Notifier
{
    public static async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var script =
            $"display notification \"{TerminalLauncher.EscapeForAppleScript(message)}\" "
            + $"with title \"Hypercode\" subtitle \"{TerminalLauncher.EscapeForAppleScript(title)}\"";

        try
        {
            await ProcessRunner.RunAsync("/usr/bin/osascript", new[] { "-e", script }, null, TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // O aviso é extra: a linha continua destacada na lista.
        }
    }
}
