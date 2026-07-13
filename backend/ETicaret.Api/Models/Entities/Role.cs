namespace ETicaret.Api.Models.Entities;

public class Role
{
    public int Id { get; set; }
    public string Name {get; set; } = string.Empty;
}


// Taslaktaki iki rolü (Admin/Customer) satır olarak tutacak.
// Rolleri koda gömmek yerine tablo yapmamızın nedeni genişleyebilirlik 