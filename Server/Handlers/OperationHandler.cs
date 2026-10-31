using Server.Replication;
using Server.Services;
using Shared;
using System.Text.Json;

namespace Server.Handlers
{
    public class OperationHandler
    {
        private readonly IEducationService _service;
        private readonly ReplicationManager _replication;
        private readonly PubSubService _pubSub;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OperationHandler(IEducationService service, ReplicationManager replication, PubSubService pubSub)
        {
            _service = service;
            _replication = replication;
            _pubSub = pubSub;
        }

        public async Task<Response> Handle(Request request)
        {
            try
            {
                return request.Operation switch
                {
                    "EnrollStudent" => await Run<EnrollStudentPayload>(request.Data,
                        p => _service.EnrollStudent(p.StudentId, p.CourseId)),
                    "GetCourse" => await Run<IdPayload>(request.Data,
                        p => _service.GetCourse(p.Id)),
                    "SubmitAssignment" => await Run<IdPayload>(request.Data,
                        p => _service.SubmitAssignment(p.Id)),
                    "CompleteCourse" => await Run<CompleteCoursePayload>(request.Data,
                        p => _service.CompleteCourse(p.StudentId, p.CourseId)),
                    "GetServerTime" => await _service.GetServerTime(),
                    "GetStatus" => _replication.GetStatus(),
                    "CompareReplicas" => _replication.CompareReplicas(),
                    "RecoverPrimary" => _replication.Recover(),
                    "GetSubscribers" => _pubSub.GetSubscribers(),
                    "SetSubscriberState" => await Run<SubscriberStatePayload>(request.Data,
                        p => Task.FromResult(_pubSub.SetSubscriberState(p.Name, p.State))),
                    "GetSubscriberCatalog" => _pubSub.GetSubscriberCatalog(),
                    "RegisterSubscriber" => await Run<NamePayload>(request.Data,
                        p => Task.FromResult(_pubSub.RegisterSubscriber(p.Name))),
                    "GetBrokers" => _pubSub.GetBrokers(),
                    "SetBrokerState" => await Run<BrokerStatePayload>(request.Data,
                        p => Task.FromResult(_pubSub.SetBrokerState(p.Name, p.Available))),
                    "ProcessQueue" => await _pubSub.ProcessQueueAsync(),
                    "GetPublisherErrors" => _pubSub.GetPublisherErrors(),
                    "GetEventStats" => _pubSub.GetEventStats(),
                    _ => Response.Error($"Неизвестная операция: {request.Operation}")
                };
            }
            catch (InvalidRequestException ex)
            {
                return Response.Error(ex.Message);
            }
            catch (JsonException)
            {
                return Response.Error("Некорректный запрос: неверный формат данных");
            }
            catch (Exception ex)
            {
                return Response.Error($"Ошибка сервера: {ex.Message}");
            }
        }

        private static Task<Response> Run<T>(string? data, Func<T, Task<Response>> action) where T : class
            => action(Deserialize<T>(data));

        private static T Deserialize<T>(string? data) where T : class
        {
            if (data == null)
                throw new InvalidRequestException("Данные запроса отсутствуют");

            return JsonSerializer.Deserialize<T>(data, _jsonOptions)
                ?? throw new InvalidRequestException("Неверный формат данных");
        }
    }

    internal class InvalidRequestException(string message) : Exception(message);

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

    internal class SubscriberStatePayload
    {
        public string Name { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    internal class NamePayload
    {
        public string Name { get; set; } = string.Empty;
    }

    internal class BrokerStatePayload
    {
        public string Name { get; set; } = string.Empty;
        public bool Available { get; set; }
    }
}