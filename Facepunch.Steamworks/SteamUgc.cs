﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Steamworks.Data;

namespace Steamworks
{
	/// <summary>
	/// Functions for accessing and manipulating Steam user information.
	/// This is also where the APIs for Steam Voice are exposed.
	/// </summary>
	public class SteamUGC : SteamSharedClass<SteamUGC>
	{
		internal static ISteamUGC Internal => Interface as ISteamUGC;

		internal override bool InitializeInterface( bool server )
		{
			SetInterface( server, new ISteamUGC( server ) );
			if ( Interface.Self == IntPtr.Zero ) return false;

			InstallEvents( server );

			return true;
		}

		internal static void InstallEvents( bool server )
		{
			Dispatch.Install<DownloadItemResult_t>( x => OnDownloadItemResult?.Invoke( x.AppID.Value, x.PublishedFileId, x.Result ), server );
			Dispatch.Install<RemoteStoragePublishedFileSubscribed_t>( x => OnItemSubscribed?.Invoke( x.AppID.Value, x.PublishedFileId ), server );
			Dispatch.Install<RemoteStoragePublishedFileUnsubscribed_t>( x => OnItemUnsubscribed?.Invoke( x.AppID.Value, x.PublishedFileId ), server );
			Dispatch.Install<ItemInstalled_t>( x => OnItemInstalled?.Invoke( x.AppID.Value, x.PublishedFileId ), server );
		}

		/// <summary>
		/// Invoked after an item is downloaded.
		/// </summary>
		public static event Action<AppId, PublishedFileId, Result> OnDownloadItemResult;
		
		/// <summary>
		/// Invoked when a new item is subscribed.
		/// </summary>
		public static event Action<AppId, PublishedFileId> OnItemSubscribed;
		public static event Action<AppId, PublishedFileId> OnItemUnsubscribed;
		public static event Action<AppId, PublishedFileId> OnItemInstalled;

		public static async Task<bool> DeleteFileAsync( PublishedFileId fileId )
		{
			var r = await Internal.DeleteItem( fileId );
			return r?.Result == Result.OK;
		}

		/// <summary>
		/// Start downloading this item. You'll get notified of completion via <see cref="OnDownloadItemResult"/>.
		/// </summary>
		/// <param name="fileId">The ID of the file to download.</param>
		/// <param name="highPriority">If <see langword="true"/> this should go straight to the top of the download list.</param>
		/// <returns><see langword="true"/> if nothing went wrong and the download is started.</returns>
		public static bool Download( PublishedFileId fileId, bool highPriority = false )
		{
			return Internal.DownloadItem( fileId, highPriority );
		}

		/// <summary>
		/// Will attempt to download this item asyncronously - allowing you to instantly react to its installation.
		/// </summary>
		/// <param name="appId"></param>
		/// <param name="fileId">The ID of the file you download.</param>
		/// <param name="progress">An optional callback</param>
		/// <param name="ct">Allows to send a message to cancel the download anywhere during the process.</param>
		/// <param name="milisecondsUpdateDelay">How often to call the progress function.</param>
		/// <returns><see langword="true"/> if downloaded and installed properly.</returns>
		public static async Task<bool> DownloadAsync( AppId appId, PublishedFileId fileId, Action<float, long, long> progress = null, int milisecondsUpdateDelay = 60, CancellationToken ct = default )
		{
			var item = new Steamworks.Ugc.Item( fileId );

			progress?.Invoke( 0.0f , 0, 0);

			Result result = Result.None;

			Action<AppId, PublishedFileId, Result> onDownloadStarted = ( appIdCallback, fileIdInCallback, resultInCallback ) =>
			{
				if ( appIdCallback == appId && fileIdInCallback == fileId )
					result = resultInCallback;
			};

			SteamUGC.OnDownloadItemResult += onDownloadStarted;
			if ( SteamUGC.Download( fileId, true ) == false )
			{
				SteamUGC.OnDownloadItemResult -= onDownloadStarted;
				return item.IsInstalled;
			}

			try
			{
				while ( true )
				{
					if ( ct != default && ct.IsCancellationRequested )
						break;

					if ( !item.IsDownloading )
						progress?.Invoke( 0.1f , 0, 0);
					else
						progress?.Invoke( 0.1f + item.DownloadAmount * 0.85f , item.DownloadBytesDownloaded, item.DownloadBytesTotal);

					if ( !item.IsDownloading && item.IsInstalled && result != Result.None )
						break;

					await Task.Delay( milisecondsUpdateDelay );
				}

				if ( result != Result.OK && result != Result.None )
					return false;

				if ( ct.IsCancellationRequested )
					return false;
			}
			finally
			{
				SteamUGC.OnDownloadItemResult -= onDownloadStarted;
			}

			progress?.Invoke( 1.0f, item.DownloadBytesTotal, item.DownloadBytesTotal);
			return item.IsInstalled;
		}

		/// <summary>
		/// Utility function to fetch a single item. Internally this uses <c>Ugc.FileQuery</c> -
		/// which you can use to query multiple items if you need to.
		/// </summary>
		public static async Task<Ugc.Item?> QueryFileAsync( PublishedFileId fileId )
		{
			var result = await Ugc.Query.All
									.WithFileId( fileId )
									.GetPageAsync( 1 );

			if ( !result.HasValue || result.Value.ResultCount != 1 )
				return null;

			var item = result.Value.Entries.First();

			result.Value.Dispose();

			return item;
		}

		public static async Task<bool> StartPlaytimeTracking(PublishedFileId fileId)
		{
			var result = await Internal.StartPlaytimeTracking(new[] {fileId}, 1);
			return result.Value.Result == Result.OK;
		}
		
		public static async Task<bool> StopPlaytimeTracking(PublishedFileId fileId)
		{
			var result = await Internal.StopPlaytimeTracking(new[] {fileId}, 1);
			return result.Value.Result == Result.OK;
		}
		
		public static async Task<bool> StopPlaytimeTrackingForAllItems()
		{
			var result = await Internal.StopPlaytimeTrackingForAllItems();
			return result.Value.Result == Result.OK;
		}

		public static uint GetSubscribedItems(List<PublishedFileId> subscribedItems)
		{
			if (subscribedItems == null) return 0;

			uint numItems = Internal.GetNumSubscribedItems();
			PublishedFileId[] items = new PublishedFileId[numItems];
			numItems = Internal.GetSubscribedItems( items, numItems );
			for ( int i = 0; i < numItems; i++ )
				subscribedItems.Add( items[i] );
			return numItems;
		}

		/// <summary>
		/// Suspends all workshop downloads.
		/// Downloads will be suspended until you resume them by calling <see cref="ResumeDownloads"/> or when the game ends.
		/// </summary>
		public static void SuspendDownloads() => Internal.SuspendDownloads(true);

		/// <summary>
		/// Resumes all workshop downloads.
		/// </summary>
		public static void ResumeDownloads() => Internal.SuspendDownloads(false);

		/// <summary>
		/// Show the app's latest Workshop EULA to the user in an overlay window, where they can accept it or not.
		/// </summary>
		public static bool ShowWorkshopEula()
		{
			return Internal.ShowWorkshopEULA();
		}

		/// <summary>
		/// Retrieve information related to the user's acceptance or not of the app's specific Workshop EULA.
		/// </summary>
		public static async Task<bool?> GetWorkshopEulaStatus()
		{
			var status = await Internal.GetWorkshopEULAStatus();
			return status?.Accepted;
		}

	}
}
