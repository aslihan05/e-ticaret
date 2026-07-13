namespace ETicaret.Api.Models.Entities;

public class Product
{
    public int Id {get; set;}
    public string Name {get; set; } = string.Empty;
    public string? Description {get; set;}
    public decimal Price { get; set; }  // decimal finansal hesaplamalarda daha çok tercih edilir
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    
    
    public bool IsActive {get; set; } = true;
    // Silmek yerine pasife çekme imkanı.
    // Productscontroller'ın sadece aktifleri listelemesi.

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}


// Kataloğun merkezi.