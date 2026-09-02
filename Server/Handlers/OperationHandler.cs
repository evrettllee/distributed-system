using Server.Services;
using Shared;
using System.Text.Json;

namespace Server.Handlers
{
    public class OperationHandler
    {
        private readonly IEducationService _service;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OperationHandler(IEducationService service)
        {
            _service = service;
        }

        public Response Handle(Request request)
        {
            try
            {
                return request.Operation switch
                {
                    "EnrollStudent" => HandleEnrollStudent(request.Data),
                    "GetCourse" => HandleGetCourse(request.Data),
                    "SubmitAssignment" => HandleSubmitAssignment(request.Data),
                    "CompleteCourse" => HandleCompleteCourse(request.Data),
                    _ => Response.Error($"Неизвестная операция: {request.Operation}")
                };
            }
            catch (Exception ex)
            {
                return Response.Error($"Ошибка сервера: {ex.Message}");
            }
        }

        private Response HandleEnrollStudent(string? data)
        {
            if (data == null)
            {
                return Response.Error("Данные запроса отсутствуют");
            }

            var payload = JsonSerializer.Deserialize<EnrollStudentPayload>(data, _jsonOptions);
            if (payload == null)
            {
                return Response.Error("Неверный формат данных");
            }

            return _service.EnrollStudent(payload.StudentId, payload.CourseId);
        }

        private Response HandleGetCourse(string? data)
        {
            if (data == null)
            {
                return Response.Error("Данные запроса отсутствуют");
            }

            var payload = JsonSerializer.Deserialize<IdPayload>(data, _jsonOptions);
            if (payload == null)
            {
                return Response.Error("Неверный формат данных");
            }

            return _service.GetCourse(payload.Id);
        }

        private Response HandleSubmitAssignment(string? data)
        {
            if (data == null)
            {
                return Response.Error("Данные запроса отсутствуют");
            }

            var payload = JsonSerializer.Deserialize<IdPayload>(data, _jsonOptions);
            if (payload == null)
            {
                return Response.Error("Неверный формат данных");
            }

            return _service.SubmitAssignment(payload.Id);
        }

        private Response HandleCompleteCourse(string? data)
        {
            if (data == null)
            {
                return Response.Error("Данные запроса отсутствуют");
            }

            var payload = JsonSerializer.Deserialize<CompleteCoursePayload>(data, _jsonOptions);
            if (payload == null)
            {
                return Response.Error("Неверный формат данных");
            }

            return _service.CompleteCourse(payload.StudentId, payload.CourseId);
        }
    }

    internal class IdPayload
    {
        public int Id { get; set; }
    }

    internal class EnrollStudentPayload
    {
        public int StudentId { get; set; }
        public int CourseId { get; set; }
    }

    internal class CompleteCoursePayload
    {
        public int StudentId { get; set; }
        public int CourseId { get; set; }
    }
}