namespace ETicaret.Api.Models.Dtos;

// Kullanıcının yorum yazarken/güncellerken gönderdiği gövde.
public class ReviewDto
{
    public int Rating { get; set; }        // 1-5
    public string? Comment { get; set; }   // Opsiyonel: yalnız yıldız verilebilir
}
