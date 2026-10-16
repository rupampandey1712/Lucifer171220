using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SkiaSharp;
using StaySphere.Contracts.Engagement;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MessagingAttachmentTests(ApiFactory api)
{
    private async Task<(HttpClient Guest, HttpClient Host, Guid ConversationId)> ConversationAsync()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guest, _) = await api.RegisterAsync();
        var conversation = await (await guest.PostJsonAsync("/api/v1/conversations", new StartConversationRequest(propertyId, null, "Hello!")))
            .ReadAsync<ConversationDto>();
        return (guest, hostClient, conversation.Id);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid conversationId, byte[] bytes, string fileName, string? caption = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (caption is not null) form.Add(new StringContent(caption), "caption");
        return client.PostAsync($"/api/v1/conversations/{conversationId}/attachments", form);
    }

    [Fact]
    public async Task Images_are_re_encoded_and_only_participants_can_download_them()
    {
        var (guest, host, conversationId) = await ConversationAsync();

        var sent = await UploadAsync(guest, conversationId, Png(), "../../etc/passwd photo.png", "Our parking spot");
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var message = await sent.ReadAsync<MessageDto>();
        message.Body.ShouldBe("Our parking spot");
        message.Attachment.ShouldNotBeNull();
        message.Attachment.ContentType.ShouldBe("image/jpeg", "uploads are re-encoded");
        message.Attachment.FileName.ShouldBe("passwd photo.jpg", "directory parts are dropped from file names");

        var hostDownload = await host.GetAsync(message.Attachment.Url);
        hostDownload.StatusCode.ShouldBe(HttpStatusCode.OK);
        hostDownload.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        hostDownload.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await hostDownload.Content.ReadAsByteArrayAsync()).Take(2).ShouldBe(new byte[] { 0xFF, 0xD8 });

        // Stored outside the publicly served /media folder.
        var mediaRoot = Path.Combine(Path.GetTempPath(), "staysphere-tests-media");
        Directory.Exists(Path.Combine(mediaRoot, "private-message-attachments")).ShouldBeFalse();
        Directory.GetFiles(Path.Combine(mediaRoot + "-private", "private-message-attachments", conversationId.ToString("N"))).ShouldNotBeEmpty();

        var (stranger, _) = await api.RegisterAsync();
        (await stranger.GetAsync(message.Attachment.Url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.CreateClient().GetAsync(message.Attachment.Url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await UploadAsync(stranger, conversationId, Png(), "x.png")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var history = await (await host.GetAsync($"/api/v1/conversations/{conversationId}/messages")).ReadAsync<Contracts.PagedResult<MessageDto>>();
        history.Items.ShouldContain(m => m.Attachment != null && m.Attachment.Url == message.Attachment.Url);
    }

    [Fact]
    public async Task Clean_pdfs_are_accepted_and_active_or_disguised_files_are_rejected()
    {
        var (guest, _, conversationId) = await ConversationAsync();

        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF");
        var ok = await UploadAsync(guest, conversationId, pdf, "itinerary.pdf");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var message = await ok.ReadAsync<MessageDto>();
        var download = await guest.GetAsync(message.Attachment!.Url);
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment", "PDFs download instead of rendering in our origin");

        var scripted = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /OpenAction << /S /JavaScript /JS (app.alert(1)) >> >> endobj\n%%EOF");
        (await UploadAsync(guest, conversationId, scripted, "invoice.pdf")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        (await UploadAsync(guest, conversationId, html, "photo.png")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await UploadAsync(guest, conversationId, new byte[6 * 1024 * 1024 - 1024], "big.pdf")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.RequestEntityTooLarge);
    }
}
