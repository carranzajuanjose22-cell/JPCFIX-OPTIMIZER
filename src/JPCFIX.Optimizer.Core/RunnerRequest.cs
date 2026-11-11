namespace JPCFIX.Optimizer.Core;

public enum RunnerAction { Apply, Rollback, RestorePoint }
public record RunnerRequest(RunnerAction Action, Guid Original, Guid Target, Guid Operation)
{
    public static RunnerRequest Parse(string[] args) => args.Length switch
    {
        1 when args[0] == "restore-point" => new(RunnerAction.RestorePoint, default, default, default),
        2 when args[0] == "rollback" => new(RunnerAction.Rollback, default, default, ParseId(args[1])),
        3 when args[0] == "apply" => new(RunnerAction.Apply, ParseId(args[1]), ParseId(args[2]), default),
        _ => throw new InvalidOperationException("Acción no autorizada. Solo se aceptan acciones tipadas y GUID válidos.")
    };
    private static Guid ParseId(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
        ? id : throw new InvalidOperationException("Identificador inválido.");
}
