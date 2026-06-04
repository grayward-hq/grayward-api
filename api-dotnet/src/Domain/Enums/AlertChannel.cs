namespace Domain.Enums;

[Flags]
public enum AlertChannel 
{ 
    None  = 0,
    Email = 1,
    Slack = 2,
    Push  = 4
}
