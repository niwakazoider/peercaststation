using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace PeerCastStation.Core
{
  public class BroadcastChannel
    : Channel
  {
    public override bool IsBroadcasting { get { return true; } }
    public ISourceStreamFactory SourceStreamFactory { get; private set; }
    public IContentReaderFactory ContentReaderFactory { get; private set; }
    private Timer postStreamPositionTimer = null;

    public BroadcastChannel(
        PeerCast peercast,
        Guid channel_id,
        ChannelInfo channel_info,
        ISourceStreamFactory source_stream_factory,
        IContentReaderFactory content_reader_factory)
      : base(peercast, channel_id)
    {
      this.ChannelInfo = channel_info;
      this.SourceStreamFactory = source_stream_factory;
      this.ContentReaderFactory = content_reader_factory;
      var postTimerDelegate = new TimerCallback(o=> {PostStreamPosition();});
      this.postStreamPositionTimer = new Timer(postTimerDelegate, null, 5000, 30000);
      Closed += (sender, e) => {
        postStreamPositionTimer.Dispose();
      };
    }

    public override void Start(Uri source_uri)
    {
      var source_factory = this.SourceStreamFactory;
      if (source_factory==null) {
        source_factory = PeerCast.SourceStreamFactories.FirstOrDefault(factory => source_uri.Scheme==factory.Scheme);
        if (source_factory==null) {
          logger.Error("Protocol `{0}' is not found", source_uri.Scheme);
          throw new ArgumentException(String.Format("Protocol `{0}' is not found", source_uri.Scheme));
        }
      }
      var content_reader = ContentReaderFactory.Create(this);
      var source_stream = source_factory.Create(this, source_uri, content_reader);
      this.Start(source_uri, source_stream);
    }

    static public Guid CreateChannelID(Guid bcid, string channel_name, string genre, string source)
    {
      var stream = new System.IO.MemoryStream();
      using (var writer = new System.IO.BinaryWriter(stream)) {
        var bcid_hash = System.Security.Cryptography.SHA512.Create().ComputeHash(bcid.ToByteArray());
        writer.Write(bcid_hash);
        writer.Write(channel_name);
        writer.Write(genre);
        writer.Write(source);
      }
      var channel_hash = System.Security.Cryptography.MD5.Create().ComputeHash(stream.ToArray());
      return new Guid(channel_hash);
    }

    public void PostStreamPosition()
    {
      if (IsBroadcasting && Contents.Newest != null && Nodes.Count>0) {
        Broadcast(null, CreateStreamPositionPacket(), BroadcastGroup.Relays);
        logger.Debug("Send BCST MSG: stream position {0}", Contents.Newest.Position.ToString());
      }
    }

    private Atom CreateStreamPositionPacket()
    {
      var atoms = new AtomCollection();
      atoms.SetChanInfoStreamPosition(Contents.Newest.Position.ToString());
      var bcst = new AtomCollection();
      bcst.SetBcstFrom(PeerCast.SessionID);
      bcst.SetBcstGroup(BroadcastGroup.Relays);
      bcst.SetBcstHops(0);
      bcst.SetBcstTTL(12);
      bcst.SetBcstChannelID(ChannelID);
      bcst.Add(new Atom(Atom.PCP_CHAN_INFO_STREAMPOSITION, atoms));
      return new Atom(Atom.PCP_BCST, bcst);
    }
  }

}
