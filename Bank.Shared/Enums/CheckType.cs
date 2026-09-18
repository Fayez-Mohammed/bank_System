namespace Bank.Shared.Enums;

// DBML note lists "live_voice, manual_7boxes" but the column default is
// "live". Both are kept here so no valid existing value is lost;
// confirm with the team which set is authoritative and drop the other.
public enum CheckType
{
    Live = 0,
    LiveVoice = 1,
    Manual7Boxes = 2
}
