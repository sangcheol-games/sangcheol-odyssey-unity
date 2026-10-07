namespace SCOdyssey.Audio
{
    public interface IMixBus
    {
        float Volume { get; set; }              // 선형 0~1
    }

    public interface IAudioMixer
    {
        IMixBus Master { get; }
        IMixBus Music { get; }
        IMixBus HitSound { get; }
        IMixBus Sfx { get; }
    }
}
