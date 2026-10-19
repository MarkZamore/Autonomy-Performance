public sealed class StepResult
{
    public string Name,Status,Detail;
    public StepResult() { }
    public StepResult(string name,string status,string detail){Name=name;Status=status;Detail=detail;}
}
