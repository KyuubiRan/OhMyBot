namespace OhMyBot.Core.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
