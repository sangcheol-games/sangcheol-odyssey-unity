namespace SCOdyssey.Audio
{
    public enum AudioLoadStatus
    {
        Ok,
        NotFound,
        DecodeError,
        Timeout,
        Cancelled,
        Superseded,             // 더 나중 요청이 들어와 버려짐
        EngineUnavailable
    }

    public readonly struct AudioLoadResult
    {
        public readonly AudioLoadStatus Status;
        public readonly string Detail;

        public AudioLoadResult(AudioLoadStatus status, string detail)
        {
            Status = status;
            Detail = detail;
        }

        public bool Ok
        {
            get { return Status == AudioLoadStatus.Ok; }
        }
    }
}
