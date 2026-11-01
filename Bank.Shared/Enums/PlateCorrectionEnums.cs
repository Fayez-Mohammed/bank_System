namespace Bank.Shared.Enums;

public enum PlateCorrectionAction
{
    Edit,
    Delete,
    Add,
    // Session-level rows (no plate): the user confirmed the session / the session was re-transcribed.
    Reviewed,
    Reprocessed
}

public enum PlateCorrectionSessionType
{
    Recording,
    Live
}
