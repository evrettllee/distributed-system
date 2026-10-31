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
        private readonly ConsensusService _consensus;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OperationHandler(IEducationService service, ReplicationManager replication, PubSubService pubSub, ConsensusService consensus)
        {
            _service = service;
            _replication = replication;
            _pubSub = pubSub;
            _consensus = consensus;
        }

        public async Task<Response> Handle(Request request)
        {
            try
            {
                return request.Operation switch
                {
                    "EnrollStudent" => await RunAsync<StudentCoursePayload>(request.Data,
                        p => _service.EnrollStudent(p.StudentId, p.CourseId)),
                    "GetCourse" => await RunAsync<IdPayload>(request.Data,
                        p => _service.GetCourse(p.Id)),
                    "SubmitAssignment" => await RunAsync<IdPayload>(request.Data,
                        p => _service.SubmitAssignment(p.Id)),
                    "CompleteCourse" => await RunAsync<StudentCoursePayload>(request.Data,
                        p => _service.CompleteCourse(p.StudentId, p.CourseId)),
                    "GetServerTime" => await _service.GetServerTime(),
                    "GetStatus" => _replication.GetStatus(),
                    "CompareReplicas" => _replication.CompareReplicas(),
                    "RecoverPrimary" => _replication.Recover(),
                    "GetSubscribers" => _pubSub.GetSubscribers(),
                    "SetSubscriberState" => Run<SubscriberStatePayload>(request.Data,
                        p => _pubSub.SetSubscriberState(p.Name, p.State)),
                    "GetSubscriberCatalog" => _pubSub.GetSubscriberCatalog(),
                    "RegisterSubscriber" => Run<NamePayload>(request.Data,
                        p => _pubSub.RegisterSubscriber(p.Name)),
                    "GetBrokers" => _pubSub.GetBrokers(),
                    "SetBrokerState" => Run<AvailabilityPayload>(request.Data,
                        p => _pubSub.SetBrokerState(p.Name, p.Available)),
                    "ProcessQueue" => await _pubSub.ProcessQueueAsync(),
                    "GetPublisherErrors" => _pubSub.GetPublisherErrors(),
                    "GetEventStats" => _pubSub.GetEventStats(),
                    "GetNodes" => _consensus.GetNodes(),
                    "SetNodeState" => Run<AvailabilityPayload>(request.Data,
                        p => _consensus.SetNodeState(p.Name, p.Available)),
                    "SetNodeVote" => Run<NodeVotePayload>(request.Data,
                        p => _consensus.SetNodeVote(p.Name, p.VoteNo)),
                    "RunElection" => _consensus.RunElection(),
                    "CheckHeartbeat" => _consensus.CheckHeartbeat(),
                    "ProposeCourseStatus" => await RunAsync<CourseStatusPayload>(request.Data,
                        p => _consensus.ProposeCourseStatus(p.CourseId, p.Status)),
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

        private static Response Run<T>(string? data, Func<T, Response> action) where T : class
            => action(Deserialize<T>(data));

        private static Task<Response> RunAsync<T>(string? data, Func<T, Task<Response>> action) where T : class
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
}
