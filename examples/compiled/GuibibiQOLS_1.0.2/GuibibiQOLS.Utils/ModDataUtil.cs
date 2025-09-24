using System;
using System.IO;
using Vintagestory.API.Common;

namespace GuibibiQOLS.Utils;

public static class ModDataUtil
{
	public const string ModConfigFilename = "guibibiQOL.json";

	public static string GetWorldId(this ICoreAPI api)
	{
		if (api == null)
		{
			return null;
		}
		IWorldAccessor world = api.World;
		if (world == null)
		{
			return null;
		}
		return world.SavegameIdentifier.ToString();
	}

	public static T LoadOrCreateConfig<T>(this ICoreAPI api, string file, ILogger logger, T? defaultConfig = null) where T : class, new()
	{
		if (logger == null)
		{
			logger = api.Logger;
		}
		T config = defaultConfig;
		try
		{
			config = ((ICoreAPICommon)api).LoadModConfig<T>(file);
		}
		catch (Exception ex)
		{
			logger.Error("Failed loading file ({0}), error {1}. Will initialize new one", new object[2] { file, ex });
		}
		if (config == null)
		{
			config = new T();
		}
		((ICoreAPICommon)api).StoreModConfig<T>(config, file);
		return config;
	}

	public static T? LoadDataFile<T>(this ICoreAPI api, string file, ILogger logger)
	{
		if (logger == null)
		{
			logger = api.Logger;
		}
		try
		{
			if (File.Exists(file))
			{
				return JsonUtil.FromString<T>(File.ReadAllText(file));
			}
		}
		catch (Exception ex)
		{
			logger.Error("Failed loading file ({0}), error {1}", new object[2] { file, ex });
		}
		return default(T);
	}

	public static T LoadOrCreateDataFile<T>(this ICoreAPI api, string file, ILogger logger) where T : class, new()
	{
		if (logger == null)
		{
			logger = api.Logger;
		}
		T data = api.LoadDataFile<T>(file, logger);
		if (data == null)
		{
			logger.Notification("Will initialize new data file");
			data = new T();
			api.SaveDataFile(file, data, logger);
		}
		return data;
	}

	public static void SaveDataFile<T>(this ICoreAPI api, string file, T data, ILogger logger)
	{
		if (logger == null)
		{
			logger = api.Logger;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file));
			string content = JsonUtil.ToString<T>(data);
			File.WriteAllText(file, content);
		}
		catch (Exception ex)
		{
			logger.Error("Failed saving file ({0}), error {1}", new object[2] { file, ex });
		}
	}
}
