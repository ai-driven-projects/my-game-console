using System.Text;

namespace MyGameConsole.App;

/// <summary>
/// Leitor do formato texto KeyValues (VDF) da Valve: <c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>,
/// <c>localconfig.vdf</c>. Cada nó é um dicionário (chaves sem diferenciar maiúsculas) cujos valores são
/// texto ou outro nó. Tolerante: um arquivo truncado devolve o que deu para ler.
/// </summary>
public sealed class Vdf
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, Vdf>> Children =>
        _values.Where(kv => kv.Value is Vdf).Select(kv => new KeyValuePair<string, Vdf>(kv.Key, (Vdf)kv.Value));

    public string? this[string key] => _values.TryGetValue(key, out var v) ? v as string : null;

    public Vdf? Node(string key) => _values.TryGetValue(key, out var v) ? v as Vdf : null;

    /// <summary>Segue um caminho de nós (ex.: "Software", "Valve", "Steam", "apps").</summary>
    public Vdf? Path(params string[] keys)
    {
        Vdf? node = this;
        foreach (var k in keys) node = node?.Node(k);
        return node;
    }

    public long Long(string key) => long.TryParse(this[key], out var v) ? v : 0;

    /// <summary>Lê o arquivo, ou devolve null se ele não existir ou não puder ser aberto.</summary>
    public static Vdf? Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static Vdf Parse(string text)
    {
        int pos = 0;
        var root = new Vdf();
        ReadInto(root, text, ref pos);
        return root;
    }

    private static void ReadInto(Vdf node, string text, ref int pos)
    {
        while (NextToken(text, ref pos, out var token, out bool quoted))
        {
            if (!quoted && token == "}") return;
            if (!quoted && token == "{") continue; // chave sem nome: ignora

            var key = token;
            if (!NextToken(text, ref pos, out var value, out bool valueQuoted)) return;

            if (!valueQuoted && value == "{")
            {
                var child = new Vdf();
                ReadInto(child, text, ref pos);
                node._values[key] = child;
            }
            else
            {
                node._values[key] = value;
            }
        }
    }

    private static bool NextToken(string text, ref int pos, out string token, out bool quoted)
    {
        token = string.Empty;
        quoted = false;

        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }

            break;
        }

        if (pos >= text.Length) return false;

        if (text[pos] is '{' or '}')
        {
            token = text[pos++].ToString();
            return true;
        }

        var sb = new StringBuilder();
        if (text[pos] == '"')
        {
            quoted = true;
            pos++;
            while (pos < text.Length && text[pos] != '"')
            {
                char c = text[pos++];
                if (c == '\\' && pos < text.Length)
                {
                    char e = text[pos++];
                    sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
                }
                else
                {
                    sb.Append(c);
                }
            }

            pos++; // aspas de fechamento
        }
        else
        {
            // valor sem aspas (raro): vai até espaço ou chave
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"'))
            {
                sb.Append(text[pos++]);
            }

            // "[$WIN32]" e afins depois de um valor: condição de plataforma, descartada
            if (sb.Length > 0 && sb[0] == '[') return NextToken(text, ref pos, out token, out quoted);
        }

        token = sb.ToString();
        return true;
    }
}
