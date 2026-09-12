namespace MyGameConsole.Services;

/// <summary>
/// Luz branca do teclado em notebooks Lenovo Legion/IdeaPad, pela classe WMI LENOVO_LIGHTING_METHOD
/// (a mesma usada pelo Lenovo Vantage e pelo Legion Toolkit). Níveis: 0 = apagada, 1 = baixa, 2 = alta.
/// O Windows só libera essa classe para processos elevados; sem administrador, ou em outra marca,
/// os métodos falham em silêncio e retornam false. Usa o objeto COM de script do WMI para não
/// depender de pacotes extras.
/// </summary>
public sealed class KeyboardBacklightService
{
    private const string Namespace = @"root\WMI";
    private const string Query = "SELECT * FROM LENOVO_LIGHTING_METHOD";
    private const int WhiteBacklightId = 0;

    private int? _savedLevel;

    /// <summary>Apaga a luz do teclado, guardando o nível atual para <see cref="TryRestore"/>. False se não deu.</summary>
    public bool TryTurnOff()
    {
        try
        {
            var level = GetLevel();
            if (level is null) return false;

            if (level > 0)
            {
                _savedLevel = level;
                SetLevel(0);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Devolve o nível que estava antes de <see cref="TryTurnOff"/>, se houver.</summary>
    public bool TryRestore()
    {
        if (_savedLevel is not { } level) return false;
        _savedLevel = null;

        try
        {
            SetLevel(level);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static dynamic? GetMethodObject()
    {
        var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
        if (locatorType is null) return null;

        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic service = locator.ConnectServer(".", Namespace);
        dynamic set = service.ExecQuery(Query);
        return (int)set.Count == 0 ? null : set.ItemIndex(0);
    }

    private static int? GetLevel()
    {
        var obj = GetMethodObject();
        if (obj is null) return null;

        dynamic input = obj.Methods_.Item("Get_Lighting_Current_Status").InParameters.SpawnInstance_();
        input.Properties_.Item("Lighting_ID").Value = WhiteBacklightId;
        dynamic output = obj.ExecMethod_("Get_Lighting_Current_Status", input);
        return Convert.ToInt32(output.Properties_.Item("Current_Brightness_Level").Value);
    }

    private static void SetLevel(int level)
    {
        var obj = GetMethodObject() ?? throw new InvalidOperationException("Classe WMI de iluminação da Lenovo não encontrada.");

        dynamic input = obj.Methods_.Item("Set_Lighting_Current_Status").InParameters.SpawnInstance_();
        input.Properties_.Item("Lighting_ID").Value = WhiteBacklightId;
        input.Properties_.Item("Current_State_Type").Value = 1;
        input.Properties_.Item("Current_Brightness_Level").Value = level;
        obj.ExecMethod_("Set_Lighting_Current_Status", input);
    }
}
