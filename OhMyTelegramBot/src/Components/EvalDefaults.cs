using Microsoft.CodeAnalysis.Scripting;

namespace OhMyTelegramBot.Components;

public class EvalDefaults
{
    public static readonly ScriptOptions Options = ScriptOptions.Default
                                                                .AddImports("System", "System.Linq", "System.Collections.Generic", "System.Threading.Tasks")
                                                                .AddReferences(typeof(OhMyLib.OhMyDbContext).Assembly,
                                                                               typeof(OhMyTelegramBot.Application).Assembly);
}