using System.Net;
using System.Text;
using Godot;

namespace MyRLBridge.MyRLBridgeCode;

public sealed class BridgeServer
{
    private readonly HttpListener listener = new();

    public static string LatestStateJson { get; set; } = "{}";

    public void Start()
    {
        listener.Prefixes.Add("http://127.0.0.1:8765/");
        listener.Start();

        Task.Run(ListenLoop);

        GD.Print(
            "RL bridge listening on http://127.0.0.1:8765/");
    }

    private async Task ListenLoop()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                break;
            }

            try
            {
                await HandleRequest(context);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Bridge server error: {ex}");

                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch
                {
                    // Ignore response-close errors.
                }
            }
        }
    }

    private static async Task HandleRequest(
        HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        var method = context.Request.HttpMethod;

        if (path == "/state" && method == "GET")
        {
            await Respond(context, LatestStateJson);
            return;
        }

        if (path == "/action" && method == "POST")
        {
            using var reader = new StreamReader(
                context.Request.InputStream);

            var body = await reader.ReadToEndAsync();

            ActionQueue.Enqueue(body);

            await Respond(context, "{\"ok\":true}");
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }

    private static async Task Respond(
        HttpListenerContext context,
        string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;

        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}