namespace digital_wallet_application_api;

public class User
{
    public Guid Id {get; set;}
    public required string FirstName {get; set;}
    public required string LastName {get; set;}
    public required string Email {get; set;}
    public required string Password {get; set;}
    public required int PhoneNumber {get; set;}
    public required int Balance {get; set;} 

}
