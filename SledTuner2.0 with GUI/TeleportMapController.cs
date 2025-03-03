using System;
using UnityEngine;

namespace SleddersTeleporterNs
{
	// Token: 0x02000003 RID: 3
	public class TeleportMapController : MapController
	{
		// Token: 0x06000007 RID: 7 RVA: 0x00002976 File Offset: 0x00000B76
		public TeleportMapController()
		{
			base.Awake();
		}

		// Token: 0x06000008 RID: 8 RVA: 0x00002988 File Offset: 0x00000B88
		public Vector2 mapToWorldPosition(Vector2 mapPos)
		{
			return this.FCJKGOHDPDK.LEJACADIABF(mapPos);
		}

		// Token: 0x06000009 RID: 9 RVA: 0x000029A8 File Offset: 0x00000BA8
		public Vector2 worldToMapPosition(Transform transform)
		{
			return this.FCJKGOHDPDK.GMHJGHBBPHM(transform.position);
		}
	}
}
