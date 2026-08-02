namespace OhMyBot.Core.Infrastructure.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
