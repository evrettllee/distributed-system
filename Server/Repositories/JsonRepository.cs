using System.Text.Json;
using System.Reflection;

namespace Server.Repositories;

public class JsonRepository<T> : IRepository<T> where T : class
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public JsonRepository(string filePath)
    {
        _filePath = filePath;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, "[]");
        }
    }

    public List<T> GetAll()
    {
        string json = File.ReadAllText(_filePath).Trim();
        if (string.IsNullOrEmpty(json))
        {
            File.WriteAllText(_filePath, "[]");
            return new List<T>();
        }
        return JsonSerializer.Deserialize<List<T>>(json, _options) ?? new List<T>();
    }

    public T? GetById(int id)
    {
        List<T> all = GetAll();
        return all.FirstOrDefault(item => GetId(item) == id);
    }

    public void Add(T entity)
    {
        List<T> all = GetAll();

        int nextId = all.Count > 0 ? all.Max(item => GetId(item)) + 1 : 1;
        SetId(entity, nextId);

        all.Add(entity);
        Save(all);
    }

    public void Update(T entity)
    {
        List<T> all = GetAll();
        int id = GetId(entity);
        int index = all.FindIndex(item => GetId(item) == id);

        if (index == -1)
        {
            throw new KeyNotFoundException($"Сущность с id {id} не найдена");
        }

        all[index] = entity;
        Save(all);
    }

    public void Delete(int id)
    {
        List<T> all = GetAll();
        int removed = all.RemoveAll(item => GetId(item) == id);

        if (removed == 0)
        {
            throw new KeyNotFoundException($"Сущность с id {id} не найдена");
        }

        Save(all);
    }

    private void Save(List<T> entities)
    {
        string json = JsonSerializer.Serialize(entities, _options);
        File.WriteAllText(_filePath, json);
    }

    private static int GetId(T entity)
    {
        PropertyInfo? prop = typeof(T).GetProperty("Id");
        if (prop == null)
        {
            throw new InvalidOperationException($"Тип {typeof(T).Name} не содержит свойство 'Id'");
        }

        return (int)(prop.GetValue(entity) ?? 0);
    }

    private static void SetId(T entity, int id)
    {
        PropertyInfo? prop = typeof(T).GetProperty("Id");
        if (prop == null)
        {
            throw new InvalidOperationException($"Тип {typeof(T).Name} не содержит свойство 'Id'");
        }

        prop.SetValue(entity, id);
    }
}