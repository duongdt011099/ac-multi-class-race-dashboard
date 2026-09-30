using System.Globalization;
using System.Text;
using MulticlassRace.Models;

namespace MulticlassRace.Builders;

public sealed class RaceIniBuilder
{
    public string Build(
        Race race,
        SessionType sessionType,
        Driver? human,
        IReadOnlyList<Driver> drivers,
        string weather,
        double sunAngle,
        int playerStartPosition = 1,
        bool penalties = true)
    {
        var opponents = drivers.Where(d => !d.IsHuman).ToList();
        var playerCar = human?.Car?.Trim();
        var playerSkin = human?.Skin?.Trim();

        var model = "[BENCHMARK]\r\nACTIVE=0\r\n\r\n" +
            "[REPLAY]\r\nACTIVE=0\r\n\r\n" +
            "[REMOTE]\r\nACTIVE=0\r\nSERVER_IP=\r\nSERVER_PORT=\r\nNAME=\r\nTEAM=\r\nGUID=\r\nREQUESTED_CAR=\r\nPASSWORD=\r\n\r\n" +
            "[RESTART]\r\nACTIVE=0\r\n\r\n" +
            "[__PREVIEW_GENERATION]\r\nACTIVE=0\r\n\r\n";

        var builder = new StringBuilder();
        builder.Append(model);
        builder.Append("[LIGHTING]\r\n");
        builder.Append("SUN_ANGLE=").AppendLine(sunAngle.ToString("0.##", CultureInfo.InvariantCulture));
        builder.Append("CLOUD_SPEED=0.200\r\n\r\n");
        builder.Append("[RACE]\r\n");
        builder.Append("MODEL=").AppendLine(string.IsNullOrWhiteSpace(playerCar) ? "-" : playerCar);
        builder.Append("MODEL_CONFIG=\r\n");
        builder.Append("SKIN=").AppendLine(string.IsNullOrWhiteSpace(playerSkin) ? "-" : playerSkin);
        builder.Append("TRACK=").AppendLine(race.TrackName);
        builder.Append("CONFIG_TRACK=").AppendLine(race.TrackLayout ?? string.Empty);
        builder.Append("AI_LEVEL=100\r\n");
        builder.Append("CARS=").AppendLine((opponents.Count + 1).ToString());
        builder.Append("DRIFT_MODE=0\r\n");
        builder.Append("FIXED_SETUP=0\r\n");
        builder.Append("PENALTIES=").AppendLine(penalties ? "1" : "0");
        builder.Append("JUMP_START_PENALTY=0\r\n");
        builder.Append("RACE_LAPS=").AppendLine((race.NumberOfLaps ?? 0).ToString());
        builder.Append("\r\n");

        builder.Append("[OPTIONS]\r\nUSE_MPH=0\r\n\r\n");
        builder.Append("[HEADER]\r\nVERSION=2\r\n__CM_FEATURE_SET=2\r\n\r\n");
        builder.Append("[LAP_INVALIDATOR]\r\nALLOWED_TYRES_OUT=-1\r\n\r\n");

        builder.Append("[CAR_0]\r\n");
        builder.Append("SETUP=\r\n");
        builder.Append("SKIN=").AppendLine(string.IsNullOrWhiteSpace(playerSkin) ? "-" : playerSkin);
        builder.Append("MODEL=-\r\n");
        builder.Append("MODEL_CONFIG=\r\n");
        builder.Append("BALLAST=0\r\n");
        builder.Append("RESTRICTOR=0\r\n");
        builder.Append("DRIVER_NAME=").AppendLine(human?.DriverName ?? string.Empty);
        builder.Append("NATIONALITY=").AppendLine(human?.Nationality ?? string.Empty);
        builder.Append("NATION_CODE=").AppendLine(Countries.GetNationCode(human?.Nationality));
        builder.Append("\r\n");

        for (var i = 0; i < opponents.Count; i++)
        {
            var driver = opponents[i];
            builder.Append($"[CAR_{i + 1}]\r\n");
            builder.Append("MODEL=").AppendLine(driver.Car?.Trim());
            builder.Append("SKIN=").AppendLine(driver.Skin?.Trim());
            builder.Append("BALLAST=0\r\n");
            builder.Append("RESTRICTOR=0\r\n");
            builder.Append("DRIVER_NAME=").AppendLine(driver.DriverName?.Trim());
            builder.Append("NATIONALITY=").AppendLine(driver.Nationality?.Trim() ?? string.Empty);
            builder.Append("NATION_CODE=").AppendLine(Countries.GetNationCode(driver.Nationality));
            builder.Append("\r\n");
        }

        builder.Append("[GHOST_CAR]\r\nRECORDING=0\r\nPLAYING=0\r\nLOAD=0\r\nFILE=\r\nENABLED=0\r\nSECONDS_ADVANTAGE=0\r\n\r\n");
        builder.Append("[GROOVE]\r\nVIRTUAL_LAPS=10\r\nMAX_LAPS=30\r\nSTARTING_LAPS=0\r\n\r\n");
        builder.Append("[TEMPERATURE]\r\nAMBIENT=18\r\nROAD=14\r\n\r\n");
        builder.Append("[WEATHER]\r\nNAME=").AppendLine(string.IsNullOrWhiteSpace(weather) ? "3_clear" : weather.Trim());
        builder.Append("\r\n");
        builder.Append("[WIND]\r\nSPEED_KMH_MIN=5.5\r\nSPEED_KMH_MAX=5.5\r\nDIRECTION_DEG=340\r\n\r\n");
        builder.Append("[DYNAMIC_TRACK]\r\nSESSION_START=200\r\nRANDOMNESS=200\r\nLAP_GAIN=132\r\nSESSION_TRANSFER=200\r\n\r\n");

        var (sessionName, sessionTypeValue, durationMinutes) = sessionType switch
        {
            SessionType.Practice => ("Practice", 1, Math.Clamp(race.PracticeSessionMinutes ?? 20, 0, 90)),
            SessionType.Qualifying => ("Qualifying", 2, Math.Clamp(race.QualifyingSessionMinutes ?? 20, 5, 90)),
            _ => ("Race", 3, 0)
        };

        builder.Append("[SESSION_0]\r\n");
        builder.Append("NAME=").AppendLine(sessionName);
        builder.Append("TYPE=").AppendLine(sessionTypeValue.ToString());

        if (sessionType == SessionType.Race)
        {
            builder.Append("LAPS=").AppendLine((race.NumberOfLaps ?? 0).ToString());
            builder.Append("DURATION_MINUTES=0\r\n");
            builder.Append("STARTING_POSITION=").AppendLine(playerStartPosition.ToString());
            builder.Append("SPAWN_SET=START\r\n");
        }
        else
        {
            builder.Append("DURATION_MINUTES=").AppendLine(durationMinutes.ToString());
            builder.Append("SPAWN_SET=PIT\r\n");
        }

        return builder.ToString();
    }

}
