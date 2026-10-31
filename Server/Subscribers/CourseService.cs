using Server.Events;

namespace Server.Subscribers
{
    public class CourseState
    {
        public int CourseId { get; init; }
        public string Title { get; init; } = string.Empty;
        public int ActiveStudents { get; set; }
        public int TotalEnrolled { get; set; }
        public int Completed { get; set; }
    }

    public class CourseService : SubscriberBase
    {
        private readonly Dictionary<int, CourseState> _courses = new();
        private readonly Lock _lock = new();

        public CourseService()
            : base("CourseService", TimeSpan.FromSeconds(3),
                EventTypes.StudentEnrolled, EventTypes.CourseCompleted)
        {
        }

        public CourseState? GetCourseState(int courseId)
        {
            lock (_lock)
                return _courses.TryGetValue(courseId, out CourseState? state) ? state : null;
        }

        protected override Task<string> ProcessAsync(Event evt)
        {
            if (!int.TryParse(evt.Data.GetValueOrDefault("CourseId"), out int courseId))
                throw new InvalidOperationException("в событии отсутствует CourseId");

            string title = evt.Data.GetValueOrDefault("CourseTitle") ?? $"Курс {courseId}";

            lock (_lock)
            {
                if (!_courses.TryGetValue(courseId, out CourseState? state))
                {
                    state = new CourseState { CourseId = courseId, Title = title };
                    _courses[courseId] = state;
                }

                if (evt.EventType == EventTypes.StudentEnrolled)
                {
                    state.ActiveStudents++;
                    state.TotalEnrolled++;
                }
                else if (evt.EventType == EventTypes.CourseCompleted)
                {
                    state.ActiveStudents = Math.Max(0, state.ActiveStudents - 1);
                    state.Completed++;
                }

                return Task.FromResult(
                    $"курс '{state.Title}': обучается {state.ActiveStudents}, записалось {state.TotalEnrolled}, завершили {state.Completed}");
            }
        }
    }
}