using System.IO.Pipes;
using System.Text;
using DynamicIsland.Core.Ipc;

namespace DynamicIsland.Services;

static class SingleInstance
{
    const string MutexName = @"Local\DynamicIsland.SingleInstance";
    const string PipeName = "DynamicIsland.Pipe.v1";
    static Mutex? _mutex;

    public static bool TryBecomePrimary()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool created);
            if (created)
                return true;
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    public static void Forward(ClientCommand command)
    {
        var payload = command.Sanitized().ToJson();
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(250);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(payload);
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Thread.Sleep(80);
            }
        }
    }

    public static void StartServer(Action<ClientCommand> onCommand)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line) && line.Length < 8192)
                        onCommand(ClientCommand.FromJson(line).Sanitized());
                }
                catch (Exception ex)
                {
                    Log.Error("pipe", ex);
                    await Task.Delay(200).ConfigureAwait(false);
                }
            }
        });
    }
}
