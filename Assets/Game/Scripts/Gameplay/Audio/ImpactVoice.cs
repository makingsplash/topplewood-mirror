namespace Gameplay.Audio
{
    /// <summary>One impact that will sound this frame: the body and its volume scale in
    /// [<see cref="ImpactSoundTuning.MinVolume"/>; <see cref="ImpactSoundTuning.MaxVolume"/>].</summary>
    public readonly struct ImpactVoice
    {
        public readonly int BodyId;
        public readonly float VolumeScale;

        public ImpactVoice(int bodyId, float volumeScale)
        {
            BodyId = bodyId;
            VolumeScale = volumeScale;
        }
    }
}
