namespace DesktopDlnaCast.Streaming.Validation;

public sealed class MpegTsInspector
{
    public const int PacketSize = 188;
    private const long PtsWrap = 1L << 33;
    private const long PtsHalfWrap = PtsWrap >> 1;
    private readonly byte[] pendingPacket = new byte[PacketSize];
    private int pendingLength;
    private int? programMapPid;
    private int? videoPid;
    private int? audioPid;
    private long? lastRawVideoPts;
    private long? lastRawAudioPts;
    private long? lastNormalizedVideoPts;
    private long? lastNormalizedAudioPts;
    private long videoPtsEpoch;
    private long audioPtsEpoch;
    private long? currentVideoPts;
    private long? lastIdrPts;
    private int h264ZeroRun;
    private bool awaitingNalHeader;

    public bool PatSeen { get; private set; }

    public bool PmtSeen { get; private set; }

    public bool H264Seen { get; private set; }

    public bool AacSeen { get; private set; }

    public bool SpsSeen { get; private set; }

    public bool PpsSeen { get; private set; }

    public bool PtsMonotonic { get; private set; } = true;

    public long PacketCount { get; private set; }

    public long VideoPtsCount { get; private set; }

    public long AudioPtsCount { get; private set; }

    public long IdrCount { get; private set; }

    public long MaximumIdrInterval90Khz { get; private set; }

    public void Push(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            int copyLength = Math.Min(PacketSize - pendingLength, bytes.Length);
            bytes[..copyLength].CopyTo(pendingPacket.AsSpan(pendingLength));
            pendingLength += copyLength;
            bytes = bytes[copyLength..];
            if (pendingLength != PacketSize)
            {
                continue;
            }

            ParsePacket(pendingPacket);
            pendingLength = 0;
        }
    }

    public void Complete(
        bool requireAudio,
        bool requireTiming = false,
        bool allowTrailingPartialPacket = false)
    {
        if (pendingLength != 0 && !allowTrailingPartialPacket)
        {
            throw new InvalidDataException("The MPEG-TS stream ended in the middle of a packet.");
        }

        if (!PatSeen || !PmtSeen || !H264Seen || (requireAudio && !AacSeen))
        {
            throw new InvalidDataException(
                $"MPEG-TS program information is incomplete: PAT={PatSeen}, PMT={PmtSeen}, H264={H264Seen}, AAC={AacSeen}.");
        }

        if (requireTiming &&
            (!PtsMonotonic || VideoPtsCount == 0 || IdrCount == 0 || !SpsSeen || !PpsSeen ||
             (requireAudio && AudioPtsCount == 0)))
        {
            throw new InvalidDataException(
                $"MPEG-TS timing/startup evidence is incomplete: PtsMonotonic={PtsMonotonic}, " +
                $"VideoPts={VideoPtsCount}, AudioPts={AudioPtsCount}, IDR={IdrCount}, " +
                $"SPS={SpsSeen}, PPS={PpsSeen}.");
        }
    }

    private void ParsePacket(ReadOnlySpan<byte> packet)
    {
        if (packet[0] != 0x47)
        {
            throw new InvalidDataException("The MPEG-TS packet sync byte is missing.");
        }

        PacketCount++;
        bool payloadUnitStart = (packet[1] & 0x40) != 0;
        int pid = ((packet[1] & 0x1F) << 8) | packet[2];
        int adaptationControl = (packet[3] >> 4) & 0x03;
        if (adaptationControl == 0)
        {
            throw new InvalidDataException("The MPEG-TS packet has an invalid adaptation field control value.");
        }

        if ((adaptationControl & 0x01) == 0)
        {
            return;
        }

        int payloadOffset = 4;
        if ((adaptationControl & 0x02) != 0)
        {
            int adaptationLength = packet[4];
            payloadOffset += 1 + adaptationLength;
        }

        if (payloadOffset >= PacketSize)
        {
            return;
        }

        ReadOnlySpan<byte> payload = packet[payloadOffset..];
        if (videoPid == pid)
        {
            ParseElementaryPayload(payload, payloadUnitStart, isVideo: true);
            return;
        }

        if (audioPid == pid)
        {
            ParseElementaryPayload(payload, payloadUnitStart, isVideo: false);
            return;
        }

        if (!payloadUnitStart)
        {
            return;
        }

        int pointer = payload[0];
        int sectionOffset = 1 + pointer;
        if (sectionOffset + 3 > payload.Length)
        {
            return;
        }

        if (pid == 0 && payload[sectionOffset] == 0x00)
        {
            ParseProgramAssociationTable(payload, sectionOffset);
        }
        else if (programMapPid == pid && payload[sectionOffset] == 0x02)
        {
            ParseProgramMapTable(payload, sectionOffset);
        }
    }

    private void ParseProgramAssociationTable(ReadOnlySpan<byte> payload, int offset)
    {
        int sectionLength = ((payload[offset + 1] & 0x0F) << 8) | payload[offset + 2];
        int sectionEnd = offset + 3 + sectionLength;
        int entriesEnd = sectionEnd - 4;
        if (sectionLength < 9 || sectionEnd > payload.Length)
        {
            return;
        }

        for (int entryOffset = offset + 8; entryOffset + 4 <= entriesEnd; entryOffset += 4)
        {
            int programNumber = (payload[entryOffset] << 8) | payload[entryOffset + 1];
            if (programNumber == 0)
            {
                continue;
            }

            programMapPid = ((payload[entryOffset + 2] & 0x1F) << 8) | payload[entryOffset + 3];
            PatSeen = true;
            return;
        }
    }

    private void ParseProgramMapTable(ReadOnlySpan<byte> payload, int offset)
    {
        int sectionLength = ((payload[offset + 1] & 0x0F) << 8) | payload[offset + 2];
        int sectionEnd = offset + 3 + sectionLength;
        if (sectionLength < 13 || sectionEnd > payload.Length)
        {
            return;
        }

        int programInfoLength = ((payload[offset + 10] & 0x0F) << 8) | payload[offset + 11];
        int streamOffset = offset + 12 + programInfoLength;
        int streamsEnd = sectionEnd - 4;
        while (streamOffset + 5 <= streamsEnd)
        {
            byte streamType = payload[streamOffset];
            int elementaryPid = ((payload[streamOffset + 1] & 0x1F) << 8) | payload[streamOffset + 2];
            int elementaryInfoLength = ((payload[streamOffset + 3] & 0x0F) << 8) | payload[streamOffset + 4];
            if (streamType == 0x1B)
            {
                H264Seen = true;
                videoPid = elementaryPid;
            }
            else if (streamType is 0x0F or 0x11)
            {
                AacSeen = true;
                audioPid = elementaryPid;
            }

            streamOffset += 5 + elementaryInfoLength;
        }

        PmtSeen = true;
    }

    private void ParseElementaryPayload(ReadOnlySpan<byte> payload, bool payloadUnitStart, bool isVideo)
    {
        int elementaryOffset = 0;
        if (payloadUnitStart && payload.Length >= 9 &&
            payload[0] == 0 && payload[1] == 0 && payload[2] == 1)
        {
            int headerLength = payload[8];
            elementaryOffset = 9 + headerLength;
            if ((payload[7] & 0x80) != 0 && headerLength >= 5 && payload.Length >= 14 &&
                TryReadPts(payload.Slice(9, 5), out long pts))
            {
                if (isVideo)
                {
                    currentVideoPts = TrackPts(
                        pts,
                        ref lastRawVideoPts,
                        ref lastNormalizedVideoPts,
                        ref videoPtsEpoch);
                    VideoPtsCount++;
                }
                else
                {
                    _ = TrackPts(
                        pts,
                        ref lastRawAudioPts,
                        ref lastNormalizedAudioPts,
                        ref audioPtsEpoch);
                    AudioPtsCount++;
                }
            }
        }

        if (isVideo && elementaryOffset < payload.Length)
        {
            ScanH264(payload[elementaryOffset..]);
        }
    }

    private long TrackPts(
        long rawPts,
        ref long? lastRawPts,
        ref long? lastNormalizedPts,
        ref long epoch)
    {
        if (lastRawPts is long previousRaw && previousRaw - rawPts > PtsHalfWrap)
        {
            epoch += PtsWrap;
        }

        long normalized = checked(epoch + rawPts);
        if (lastNormalizedPts is long previous && normalized < previous)
        {
            PtsMonotonic = false;
        }

        lastRawPts = rawPts;
        lastNormalizedPts = normalized;
        return normalized;
    }

    private void ScanH264(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (awaitingNalHeader)
            {
                ObserveNal(value & 0x1F);
                awaitingNalHeader = false;
            }

            if (value == 0)
            {
                h264ZeroRun = Math.Min(h264ZeroRun + 1, 3);
                continue;
            }

            if (value == 1 && h264ZeroRun >= 2)
            {
                awaitingNalHeader = true;
            }

            h264ZeroRun = 0;
        }
    }

    private void ObserveNal(int nalType)
    {
        switch (nalType)
        {
            case 5:
                IdrCount++;
                if (currentVideoPts is long pts)
                {
                    if (lastIdrPts is long previous)
                    {
                        MaximumIdrInterval90Khz = Math.Max(MaximumIdrInterval90Khz, pts - previous);
                    }

                    lastIdrPts = pts;
                }

                break;
            case 7:
                SpsSeen = true;
                break;
            case 8:
                PpsSeen = true;
                break;
        }
    }

    private static bool TryReadPts(ReadOnlySpan<byte> value, out long pts)
    {
        if (value.Length < 5 || (value[0] & 0x01) == 0 || (value[2] & 0x01) == 0 ||
            (value[4] & 0x01) == 0)
        {
            pts = 0;
            return false;
        }

        pts = ((long)(value[0] & 0x0E) << 29) |
              ((long)value[1] << 22) |
              ((long)(value[2] & 0xFE) << 14) |
              ((long)value[3] << 7) |
              ((long)(value[4] & 0xFE) >> 1);
        return true;
    }
}
