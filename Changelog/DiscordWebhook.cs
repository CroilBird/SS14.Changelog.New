using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Changelog;

public static class DiscordWebhook
{
    private static readonly HttpClient Client = new();

    public const int DiscordWebhookCharacterLimit = 2000;

    /// <summary>
    /// Cuts up content from a streamreader into discord friendly chunks and sends them onwards
    /// </summary>
    /// <param name="webhookUrl">discord webhook url to send to</param>
    /// <param name="contentStreamReader">stream reader from which to read content from</param>
    /// <returns></returns>
    public static bool SendDiffInParts(string webhookUrl, StreamReader contentStreamReader)
    {
        var sb = new StringBuilder(DiscordWebhookCharacterLimit);

        var nextLine = contentStreamReader.ReadLine();

        while (nextLine is not null)
        {
            sb.Append(nextLine + "\n");

            nextLine = contentStreamReader.ReadLine();

            if (nextLine is null)
                break;

            if (sb.Length + nextLine.Length < DiscordWebhookCharacterLimit)
                continue;

            if (!SendPart(webhookUrl, sb.ToString()))
                return false;

            sb.Clear();
            sb.Append(nextLine + "\n");
        }

        if (sb.Length > 0)
            return SendPart(webhookUrl, sb.ToString());

        return true;
    }

    /// <summary>
    /// Does the actual sending of content to a discord webhook
    /// </summary>
    /// <param name="webhookUrl"></param>
    /// <param name="contentPart"></param>
    /// <returns></returns>
    public static bool SendPart(string webhookUrl, string contentPart)
    {
        var discordWebhookBody = new Dictionary<string, object>()
        {
            { "content", contentPart },
            // disallow mentions
            { "allowed_mentions", new Dictionary<string, List<string>>(){ { "parse", [] } } },
            // disable embeds
            { "flags", 1 << 2 },
        };

        var jsonContent = JsonSerializer.Serialize(discordWebhookBody);

        Console.WriteLine("Sending JSON:");
        Console.Write(jsonContent);
        Console.WriteLine();


        var attempts = 0;
        while (attempts < 20)
        {
            attempts++;

            var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl + "?wait=true");

            request.Content = new StringContent(jsonContent);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = Client.Send(request);

            Console.WriteLine($"Status: {response.StatusCode}, message: {response.Content.ReadAsStringAsync().Result}");

            if (!response.IsSuccessStatusCode)
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.BadRequest:
                        Console.WriteLine("Bad request response received, cancelling:");
                        Console.WriteLine(response.Content.ReadAsStringAsync().Result);
                        return false;
                    case HttpStatusCode.TooManyRequests:
                        Console.WriteLine("Rate limiting...");
                        // it's actually like 300ms or so but this is fine whatever sue me
                        // I'd have to parse the json here to check what number they give me and I DON'T WANT TO
                        Thread.Sleep(1000);
                        break;
                    default:
                        Console.WriteLine($"Received unexpected response status code ({response.StatusCode}), cancelling:");
                        Console.WriteLine(response.Content.ReadAsStringAsync().Result);
                        return false;
                }

                continue;
            }

            return true;
        }

        return false;
    }
}
