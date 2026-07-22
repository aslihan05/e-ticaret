using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]   // Bot yalnızca giriş yapmış müşterilere; kimlik JWT'den okunur.
public class ChatbotController : ControllerBase
{
    private readonly ChatbotService _chatbot;

    public ChatbotController(ChatbotService chatbot)
    {
        _chatbot = chatbot;
    }

    // Kimlik her zaman imzalı token'dan. İstek gövdesinden userId ALINMAZ (IDOR koruması).
    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpPost]
    public async Task<IActionResult> Send(ChatRequestDto dto, CancellationToken ct)
    {
        if (dto.Messages == null || dto.Messages.Count == 0)
        {
            return BadRequest(new { message = "Mesaj boş olamaz." });
        }

        // Aşırı uzun geçmişi kırp: hem maliyet hem kötüye kullanım sınırı. Son 20 mesaj yeter.
        var gecmis = dto.Messages.TakeLast(20).ToList();

        try
        {
            var cevap = await _chatbot.ChatAsync(CurrentUserId, gecmis, ct);
            return Ok(new { reply = cevap });
        }
        catch (Exception)
        {
            // İç detay (API anahtarı, model, endpoint) sızmasın diye jenerik mesaj.
            return StatusCode(500, new { message = "Asistan şu anda yanıt veremiyor. Lütfen biraz sonra tekrar deneyin." });
        }
    }
}
