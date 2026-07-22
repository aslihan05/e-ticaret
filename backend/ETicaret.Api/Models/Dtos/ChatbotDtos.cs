namespace ETicaret.Api.Models.Dtos;

// Frontend'den gelen sohbet mesajı (yalnızca düz metin). Araç blokları sunucuda yönetilir,
// istemciye asla taşınmaz.
public class ChatMessageDto
{
    // "user" veya "assistant"
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

public class ChatRequestDto
{
    // Sohbet geçmişi + en son kullanıcı mesajı, sırayla. Son eleman genelde "user" mesajıdır.
    public List<ChatMessageDto> Messages { get; set; } = new();
}
