using Microsoft.EntityFrameworkCore;
using Server.Common;
using Server.Data;

namespace Server.Replication
{
    public class DataInstance
    {
        private readonly DbContextOptions<EducationDbContext> _options;

        public string Name { get; }
        public InstanceState State { get; private set; } = InstanceState.Available;

        public DataInstance(string name, DbContextOptions<EducationDbContext> options)
        {
            Name = name;
            _options = options;
        }

        public string StateText => State == InstanceState.Available ? "AVAILABLE" : "UNAVAILABLE";

        public EducationDbContext CreateContext() => new(_options);

        public InstanceState CheckState()
        {
            Logger.Log($" {Name} -> CHECK");

            string? error = null;

            try
            {
                using EducationDbContext context = CreateContext();
                State = CanConnect(context) ? InstanceState.Available : InstanceState.Unavailable;
            }
            catch (Exception ex)
            {
                State = InstanceState.Unavailable;
                error = ex.Message;
            }

            if (error != null)
                Logger.Log($" {Name} -> {StateText} ({error})");
            else
                Logger.Log($" {Name} -> {StateText}");

            return State;
        }

        public bool IsAvailable() => CheckState() == InstanceState.Available;

        protected virtual bool CanConnect(EducationDbContext context)
        {
            return context.Database.CanConnect();
        }
    }
}