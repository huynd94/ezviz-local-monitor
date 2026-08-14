using EzvizLocalMonitor.Models;
using Microsoft.Data.Sqlite;

namespace EzvizLocalMonitor.Services;

public sealed class EventStore
{
    private readonly string _connectionString;

    public EventStore(string? databaseFile = null)
    {
        _connectionString = $"Data Source={databaseFile ?? DataPaths.DatabaseFile}";
    }

    public void Initialize()
    {
        DataPaths.EnsureCreated();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS detection_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                camera_id TEXT NOT NULL,
                camera_name TEXT NOT NULL,
                detected_at TEXT NOT NULL,
                confidence REAL NOT NULL,
                image_path TEXT NOT NULL,
                delivery_status TEXT NOT NULL,
                detection_source TEXT NOT NULL DEFAULT 'YOLO cục bộ',
                is_human_detection INTEGER NOT NULL DEFAULT 1,
                ai_status TEXT NOT NULL DEFAULT 'AI tắt',
                ai_motion_detected INTEGER NULL,
                ai_person_present INTEGER NULL,
                ai_confidence REAL NULL,
                ai_summary TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_detection_events_detected_at ON detection_events(detected_at DESC);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "detection_source", "TEXT NOT NULL DEFAULT 'YOLO cục bộ'");
        EnsureColumn(connection, "is_human_detection", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "ai_status", "TEXT NOT NULL DEFAULT 'AI tắt'");
        EnsureColumn(connection, "ai_motion_detected", "INTEGER NULL");
        EnsureColumn(connection, "ai_person_present", "INTEGER NULL");
        EnsureColumn(connection, "ai_confidence", "REAL NULL");
        EnsureColumn(connection, "ai_summary", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection connection, string column, string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE detection_events ADD COLUMN {column} {definition}";
        try { command.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) { }
    }

    public long Add(DetectionEvent item)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO detection_events(camera_id, camera_name, detected_at, confidence, image_path, delivery_status, detection_source, is_human_detection, ai_status, ai_motion_detected, ai_person_present, ai_confidence, ai_summary)
            VALUES ($cameraId, $cameraName, $detectedAt, $confidence, $imagePath, $deliveryStatus, $detectionSource, $isHumanDetection, $aiStatus, $aiMotionDetected, $aiPersonPresent, $aiConfidence, $aiSummary);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$cameraId", item.CameraId.ToString());
        command.Parameters.AddWithValue("$cameraName", item.CameraName);
        command.Parameters.AddWithValue("$detectedAt", item.DetectedAt.ToString("O"));
        command.Parameters.AddWithValue("$confidence", item.Confidence);
        command.Parameters.AddWithValue("$imagePath", item.ImagePath);
        command.Parameters.AddWithValue("$deliveryStatus", item.DeliveryStatus);
        command.Parameters.AddWithValue("$detectionSource", item.DetectionSource);
        command.Parameters.AddWithValue("$isHumanDetection", item.IsHumanDetection ? 1 : 0);
        command.Parameters.AddWithValue("$aiStatus", item.AiStatus);
        command.Parameters.AddWithValue("$aiMotionDetected", item.AiMotionDetected is null ? DBNull.Value : item.AiMotionDetected.Value ? 1 : 0);
        command.Parameters.AddWithValue("$aiPersonPresent", item.AiPersonPresent is null ? DBNull.Value : item.AiPersonPresent.Value ? 1 : 0);
        command.Parameters.AddWithValue("$aiConfidence", item.AiConfidence is null ? DBNull.Value : item.AiConfidence.Value);
        command.Parameters.AddWithValue("$aiSummary", item.AiSummary);
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public void UpdateAiAnalysis(long id, AiMovementAnalysis analysis)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE detection_events SET ai_status = $status, ai_motion_detected = $motion, ai_person_present = $person, ai_confidence = $confidence, ai_summary = $summary WHERE id = $id";
        command.Parameters.AddWithValue("$status", analysis.Status);
        command.Parameters.AddWithValue("$motion", analysis.MotionDetected ? 1 : 0);
        command.Parameters.AddWithValue("$person", analysis.PersonPresent ? 1 : 0);
        command.Parameters.AddWithValue("$confidence", analysis.Confidence);
        command.Parameters.AddWithValue("$summary", analysis.Summary);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateAiStatus(long id, string status)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE detection_events SET ai_status = $status WHERE id = $id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateDeliveryStatus(long id, string status)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE detection_events SET delivery_status = $status WHERE id = $id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DetectionEvent> Recent(int take = 50)
    {
        var results = new List<DetectionEvent>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, camera_id, camera_name, detected_at, confidence, image_path, delivery_status, detection_source, is_human_detection, ai_status, ai_motion_detected, ai_person_present, ai_confidence, ai_summary FROM detection_events ORDER BY detected_at DESC LIMIT $take";
        command.Parameters.AddWithValue("$take", take);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DetectionEvent
            {
                Id = reader.GetInt64(0),
                CameraId = Guid.Parse(reader.GetString(1)),
                CameraName = reader.GetString(2),
                DetectedAt = DateTimeOffset.Parse(reader.GetString(3)),
                Confidence = reader.GetDouble(4),
                ImagePath = reader.GetString(5),
                DeliveryStatus = reader.GetString(6),
                DetectionSource = reader.IsDBNull(7) ? "YOLO cục bộ" : reader.GetString(7),
                IsHumanDetection = reader.IsDBNull(8) || reader.GetInt64(8) != 0,
                AiStatus = reader.IsDBNull(9) ? "AI tắt" : reader.GetString(9),
                AiMotionDetected = reader.IsDBNull(10) ? null : reader.GetInt64(10) != 0,
                AiPersonPresent = reader.IsDBNull(11) ? null : reader.GetInt64(11) != 0,
                AiConfidence = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                AiSummary = reader.IsDBNull(13) ? string.Empty : reader.GetString(13)
            });
        }
        return results;
    }

    public void PurgeBefore(DateTimeOffset cutoff)
    {
        foreach (var item in Recent(10_000).Where(x => x.DetectedAt < cutoff))
        {
            if (File.Exists(item.ImagePath)) File.Delete(item.ImagePath);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM detection_events WHERE detected_at < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        command.ExecuteNonQuery();
    }
}
