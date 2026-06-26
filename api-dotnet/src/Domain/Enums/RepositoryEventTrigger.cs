namespace Domain.Enums;

[Flags]
public enum RepositoryEventTrigger 
{ 
    None        = 0,
    Push        = 1,
    PullRequest = 2,
}