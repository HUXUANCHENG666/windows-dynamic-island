namespace DynamicIsland.Core.LiveTranslate;

/// <summary>
/// 识别任务排队：同一句话只保留最新的出字稿，终稿不会被出字稿挤掉，
/// 上一句的终稿也不会被下一句的出字稿覆盖。
/// </summary>
public sealed class RecognitionWorkQueue
{
    readonly List<SpeechChunk> _items = new();

    public int Count => _items.Count;

    public void Enqueue(SpeechChunk chunk)
    {
        if (!chunk.Final)
        {
            if (_items.Exists(item => item.Final && item.UtteranceId == chunk.UtteranceId))
                return;
            _items.RemoveAll(item => !item.Final && item.UtteranceId <= chunk.UtteranceId);
        }
        else
        {
            _items.RemoveAll(item => !item.Final && item.UtteranceId == chunk.UtteranceId);
        }

        _items.Add(chunk);
    }

    public SpeechChunk? Dequeue()
    {
        if (_items.Count == 0)
            return null;
        var chunk = _items[0];
        _items.RemoveAt(0);
        return chunk;
    }
}
