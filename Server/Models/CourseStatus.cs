using System.Text.Json.Serialization;

namespace Server.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter<CourseStatus>))]
    public enum CourseStatus
    {
        Draft = 0,
        Active = 1,
        Closed = 2
    }

    public static class CourseStatusRules
    {
        public static bool CanChange(CourseStatus from, CourseStatus to) => (from, to) switch
        {
            (CourseStatus.Draft, CourseStatus.Active) => true,
            (CourseStatus.Draft, CourseStatus.Closed) => true,
            (CourseStatus.Active, CourseStatus.Closed) => true,
            (CourseStatus.Closed, CourseStatus.Active) => true,
            _ => false
        };
    }
}