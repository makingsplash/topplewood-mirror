import os
import logging
from slack import WebClient
from slack import RTMClient
import ssl
import certifi
import random
from start_job import start_job, create_job
import argparse
import json
from collections import defaultdict
from git import Repo


class ArgumentParserError(Exception): pass


class ThrowingArgumentParser(argparse.ArgumentParser):
    def error(self, message):
        raise ArgumentParserError(message)


ssl_context = ssl.SSLContext(ssl.PROTOCOL_SSLv23)

# Initialize a Web API client
slack_web_client = WebClient(
    token="***REMOVED-SLACK-TOKEN***", ssl=ssl_context)  # os.environ['GAMEPIE_BULDER_SLACK_BOT_TOKEN']

parser = ThrowingArgumentParser(add_help=False);
subparsers = parser.add_subparsers()
parser_build = subparsers.add_parser("build", add_help=False)
parser_add = subparsers.add_parser("add", add_help=False)


def get_projects_string():
    res = ""
    with open("./projects.json", "r") as json_file:
        projects = json.load(json_file)
        for key, value in projects.items():
            res += key + "\t - " + value + "\n"
    return res


def commit_and_push_projects_file():
    repo = Repo(".")
    # repo.heads.master.checkout()
    origin = repo.remote("origin")
    repo.active_branch.set_tracking_branch(origin.refs.master)
    repo.index.add(['projects.json'])
    repo.index.commit("updated projects.json")
    origin.push()


def get_projects_dict():
    with open("./projects.json", "r") as json_file:
        projects = json.load(json_file)
        projects = defaultdict(lambda: None, projects)
    return projects


def help_parser(args):
    if args.help:
        slack_web_client.chat_postMessage(channel="#bot-gamepie-builds",
                                          text=parser.format_help() + "\n\nДоступные проекты:\n" + get_projects_string())
    elif args.help_build:
        slack_web_client.chat_postMessage(channel="#bot-gamepie-builds",
                                          text=parser_build.format_help() + "\n\nДоступные проекты:\n" + get_projects_string())
    elif args.help_add:
        slack_web_client.chat_postMessage(channel="#bot-gamepie-builds",
                                          text=parser_add.format_help() + "\n\nДоступные проекты:\n" + get_projects_string())


def build(args):
    projects = get_projects_dict()
    proj = args.project
    if proj in projects:
        print(args)
        start_job(projects[proj], {
            "branch": args.branch,
            "configuration": args.configuration,
            "platform": args.platform,
            "clean": args.clean == "1",
            "china": args.china == "1"
        })
    else:
        raise ArgumentParserError(
            "Проект с аббревиатурой " + proj + " не найден. Доступные проекты:\n" + get_projects_string())


def add(args):
    projects = get_projects_dict()
    print(projects)
    if args.short_name in projects:
        raise ArgumentParserError("Проект с аббревиатурой " + args.short_name + " уже существует. Выберите другую.")
    try:
        new_project_name = create_job(args.git_url)
        projects[args.short_name] = new_project_name
        with open("./projects.json", "w") as json_file:
            json.dump(projects, json_file)
        commit_and_push_projects_file()
        slack_web_client.chat_postMessage(channel="#bot-gamepie-builds",
                                          text="Проект " + new_project_name + " с аббревиатурой " + args.short_name + " успешно добавлен!")
    except Exception as e:
        slack_web_client.chat_postMessage(channel="#bot-gamepie-builds", text=str(e))


@RTMClient.run_on(event='message')
def message(**payload):
    event = payload["data"]
    try:
        channel_id = event["channel"]
        user_id = event['user'] if 'user' in event.keys() else None
        text = event["text"]

        if text is None:
            return

        text = text.lower().strip("")
        print(text)
        to_parse = [s for s in text.split(" ") if s]

        if "USM4R4N2X" != user_id and len(to_parse) >= 1 and to_parse[0] in ["!", "!build", "!add"]:
            to_parse[0] = to_parse[0][1:]
            to_parse = [s for s in to_parse if s]

            try:
                args = parser.parse_args(to_parse)
                args.func(args)

            except ArgumentParserError as e:
                slack_web_client.chat_postMessage(channel="#bot-gamepie-builds", text=str(e))
    except KeyError as key_error:
        for key, value in event.items():
            print(str(key) + ": " + str(value))
        print("--------------------------------\n")

def start_rtm_client():
    try:
        rtm_client = RTMClient(token="***REMOVED-SLACK-TOKEN***", ssl=ssl_context)
        logger.debug("start RTM client")
        rtm_client.start()
    except Exception as e:
        print(e)
        return 1
    return 0


if __name__ == "__main__":
    parser.add_argument('-h', '--help', action='store_true',
                        help='show this help message')
    parser.add_argument('-hb', '--help_build', action='store_true',
                        help='show build help message')
    parser.add_argument('-ha', '--help_add', action='store_true',
                        help='show add help message')
    parser.set_defaults(func=help_parser)

    parser_build.add_argument("project", help="Название проекта, который будем билдить")
    parser_build.add_argument("platform", choices=["android", "ios"], help="Целевая платформа")
    parser_build.add_argument("configuration", choices=["dev", "release"], help="Релизный или дев билд")
    parser_build.add_argument("branch", default="master", nargs="?", help="Ветка из которой будет происходить сборка")
    parser_build.add_argument("--clean", default="0", const="1", nargs="?", choices=["1", "0"],
                              help="Очищать ли проект перед сборкой")
    parser_build.add_argument("--china", default="0", const="1", nargs="?", choices=["1", "0"],
                              help="Собирать приложение для китайского стора")
    parser_build.set_defaults(func=build)

    parser_add.add_argument("short_name", help="Краткое имя, по которому будет в дальнейшем инициироваться сборка")
    parser_add.add_argument("git_url", help="Ссылка на гит репозиторий")
    parser_add.set_defaults(func=add)

    logger = logging.getLogger()
    logger.setLevel(logging.DEBUG)
    logger.addHandler(logging.StreamHandler())

    return_code = -1
    while (return_code != 0):
    	return_code = start_rtm_client()

