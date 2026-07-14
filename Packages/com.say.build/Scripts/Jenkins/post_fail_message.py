import urllib.request
import ssl
import json
from slack import WebClient


if __name__ == "__main__":
	ssl_context = ssl.SSLContext(ssl.PROTOCOL_SSLv23)
	contents = urllib.request.urlopen("https://api.thecatapi.com/v1/images/search", context=ssl_context).read()
	parsed = json.loads(contents)
	print(str(parsed[0]["url"]))
	slack_web_client = WebClient(token="***REMOVED-SLACK-TOKEN***", ssl=ssl_context)
	slack_web_client.chat_postMessage(channel="#bot-gamepie-builds", text="Билд не собрался, но вот тебе кота\n\n" + parsed[0]["url"])