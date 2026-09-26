using System.Security.Cryptography;

namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// Senha provisória para a equipe (preceptor, professor, secretaria), que não tem o RGM como
/// senha padrão. Vale só até o primeiro acesso, onde a troca é obrigatória.
/// </summary>
public static class SenhaProvisoria
{
    // Sem 0/O, 1/l/I: a senha é ditada ou copiada de um papel.
    private const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
    private const string Digitos = "23456789";

    public const int Tamanho = 10;

    public static string Gerar()
    {
        var todos = (Letras + Digitos).ToCharArray();
        var senha = RandomNumberGenerator.GetItems<char>(todos, Tamanho - 2).ToList();
        // Garante letra e número, para a provisória também seguir a política de senha.
        senha.Add(Letras[RandomNumberGenerator.GetInt32(Letras.Length)]);
        senha.Add(Digitos[RandomNumberGenerator.GetInt32(Digitos.Length)]);

        var embaralhada = senha.ToArray();
        RandomNumberGenerator.Shuffle(embaralhada.AsSpan());
        return new string(embaralhada);
    }
}
