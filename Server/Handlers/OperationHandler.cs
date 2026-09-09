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

        public async Task<Response> Handle(Request request)
        {
            try
            {
                return request.Operation switch
                {
                    "EnrollStudent" => await HandleEnrollStudent(request.Data),
                    "GetCourse" => await HandleGetCourse(request.Data),
                    "SubmitAssignment" => await HandleSubmitAssignment(request.Data),
                    "CompleteCourse" => await HandleCompleteCourse(request.Data),
                    _ => Response.Error($"Неизвестная операция: {request.Operation}")
                };
            }
            catch (Exception ex)
            {
                return Response.Error($"Ошибка сервера: {ex.Message}");
            }
        }

        private async Task<Response> HandleEnrollStudent(string? data)
        {
            if (data == null)
                return Response.Error("Данные запроса отсутствуют");

            var payload = JsonSerializer.Deserialize<EnrollStudentPayload>(data, _jsonOptions);
            if (payload == null)
                return Response.Error("Неверный формат данных");

            return await _service.EnrollStudent(payload.StudentId, payload.CourseId);
        }

        private async Task<Response> HandleGetCourse(string? data)
        {
            if (data == null)
                return Response.Error("Данные запроса отсутствуют");

            var payload = JsonSerializer.Deserialize<IdPayload>(data, _jsonOptions);
            if (payload == null)
                return Response.Error("Неверный формат данных");

            return await _service.GetCourse(payload.Id);
        }

        private async Task<Response> HandleSubmitAssignment(string? data)
        {
            if (data == null)
                return Response.Error("Данные запроса отсутствуют");

            var payload = JsonSerializer.Deserialize<IdPayload>(data, _jsonOptions);
            if (payload == null)
                return Response.Error("Неверный формат данных");

            return await _service.SubmitAssignment(payload.Id);
        }

        private async Task<Response> HandleCompleteCourse(string? data)
        {
            if (data == null)
                return Response.Error("Данные запроса отсутствуют");

            var payload = JsonSerializer.Deserialize<CompleteCoursePayload>(data, _jsonOptions);
            if (payload == null)
                return Response.Error("Неверный формат данных");

            return await _service.CompleteCourse(payload.StudentId, payload.CourseId);
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