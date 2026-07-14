#!/usr/local/opt/python/libexec/bin/python

import sys
import json
import subprocess
import os
import requests
import time

def uploadBuildToDIAWI(app_name, path):
    
    files = {
        'token': (None, 'JjkreHGdUc2xtEOC353yMQ7BUimzqnQ'),
        'file': (path, open(path, 'rb')),
    }

    response = requests.post('https://builds.saygames.io/api/upload', files=files)

    if response.status_code == 200:
        link = response.json().get("url")
        if link is None:
            error = response.json().get("error")
            print("Error: " + error)
            return "Ërror: " + error

        print("Build was successfully uploaded")
        print("LINK: " + link)
        return link
    
    return 'Error.'


if __name__ == "__main__":
    app_name = sys.argv[1]
    path = sys.argv[2]
    env_file_path = sys.argv[3]
    link = uploadBuildToDIAWI(app_name, path)
    if link != "Error.":
        with open(env_file_path, "w") as f:
            f.write("LINK_TO_DIAWI=\"" + link + "\"")
    else:
        exit(1)
