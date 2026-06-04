namespace Application.Interfaces;

public interface ITokenService
{
    (string RawToken, string TokenHash) Generate();
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}