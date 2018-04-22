using System;

namespace Regulations.Gov.Archiver
{
    public class Persistable<T>
    {
        public string Id { get; set; }
        public DateTimeOffset RetrievedAt { get; set; }
        public T Value { get; set; }
    }
}
