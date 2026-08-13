using EzvizLocalMonitor.Models;
using Microsoft.Data.Sqlite;

namespace EzvizLocalMonitor.Services;

public sealed class EventStore
{
    private readonly string _connectionString = $"Data Source={DataPaths.DatabaseFile}";

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
                delivery_status TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_detection_events_detected_at ON detection_events(detected_at DESC);
            """;
        command.ExecuteNonQuery();
    }

    public long Add(DetectionEvent item)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO detection_events(camera_id, camera_name, detected_at, confidence, image_path, delivery_status)
            VALUES ($cameraId, $cameraName, $detectedAt, $confidence, $imagePath, $deliveryStatus);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$cameraId", item.CameraId.ToString());
        command.Parameters.AddWithValue("$cameraName", item.CameraName);
        command.Parameters.AddWithValue("$detectedAt", item.DetectedAt.ToString("O"));
        command.Parameters.AddWithValue("$confidence", item.Confidence);
        command.Parameters.AddWithValue("$imagePath", item.ImagePath);
        command.Parameters.AddWithValue("$deliveryStatus", item.DeliveryStatus);
        return (long)(command.ExecuteScalar() ?? 0L);
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
        command.CommandText = "SELECT id, camera_id, camera_name, detected_at, confidence, image_path, delivery_status FROM detection_events ORDER BY detected_at DESC LIMIT $take";
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
                DeliveryStatus = reader.GetString(6)
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
