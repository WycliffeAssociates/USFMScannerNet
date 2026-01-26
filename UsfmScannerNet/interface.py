import sys
import re
import threading
import json
# Fake ToolsConfigManager to avoid filesystem access issues
sys.path.append('usfmtools/src')

class FakeToolsConfigManager:
    def get(self, section, option, fallback=""):
        # Return default values for the options used in listener
        if section == 'VerifyUSFM':
            if option == 'source_dir':
                return ""
            elif option == 'compare_dir':
                return ""
        elif section == 'UsfmWizard' and option == 'version':
            return "unknown"
        return fallback

    def getboolean(self, section, option, fallback=False):
        return fallback

    def get_section(self, section):
        # Return a fake section proxy
        class FakeSection:
            def get(self, option, fallback=""):
                return fallback
            def getboolean(self, option, fallback=False):
                return fallback
        return FakeSection()

# Patch the module before importing

###
sys.modules['configmanager'] = type(sys)('configmanager')
sys.modules['configmanager'].ToolsConfigManager = FakeToolsConfigManager
sys.modules['usfmtools.src.configmanager'] = sys.modules['configmanager']

import usfmtools.src.verifyUSFM as verifyUSFM
from typing import Optional, Callable

class ScanResult:
    def __init__(self):
        self.results: dict[str, dict[str, list[dict[str, str]]]] = {}

    def add_error(self, book: str, chapter: str, verse: str, message: str, errorId: str):
        if book == "":
            book = "Unknown"
        if chapter == "":
            chapter = "Unknown"
        if verse == "":
            verse = "Unknown"

        if book not in self.results:
            self.results[book] = {}
        if chapter not in self.results[book]:
            self.results[book][chapter] = []
        self.results[book][chapter].append({"verse": verse, "message": message, "errorId": errorId})
    def to_json(self):
        return json.dumps(self.results)

class ResultsListener:

    def __init__(self, callback: Optional[Callable[[str], None]] = None):
        self.result = ScanResult()
        self.progress_callback = callback
    referenceRegex = r"([A-Z1-3]{2,3})\s(\d+)(:(\d+))?"
    sourceFileRegex = r"([A-Z1-3]{2,3})\.usfm"
    progress_lock = threading.Lock()
    progress_callback: Optional[Callable[[str], None]]
    result: ScanResult
    def error(self, msg:str, errorId:float):
        matches = re.findall(self.referenceRegex, msg)
        if (len(matches) > 0):
            book = matches[0][0]
            chapter = matches[0][1]
            verse = matches[0][3]
            self.result.add_error(book, chapter, verse, msg, str(errorId))
        else:
            matches = re.findall(self.sourceFileRegex, msg)
            if (len(matches) > 0):
                book = matches[0]
                self.result.add_error(book, "Unknown", "Unknown", msg, str(errorId))
            else:
                self.result.add_error("Unknown", "Unknown", "Unknown", msg, str(errorId))

    def progress(self, msg:str):
        if self.progress_callback:
            self.progress_callback(msg)
class FakeSaidWords:
    def addWord(self, word: str):
        pass

class FakeManifestYaml:
    def addProject(self, project):
        pass
    def load(self, directory: str):
        pass
    def save(self):
        pass
    

def scan_dir(directory:str) -> dict[str, dict[str, list[dict[str, str]]]]:
    verifyUSFM.config = {
        "source_dir": directory,
        "compare_dir": None,
    }
    verifyUSFM.state = verifyUSFM.State()
    verifyUSFM.std_titles = []
    verifyUSFM.listener = ResultsListener()
    verifyUSFM.saidwords = FakeSaidWords()
    verifyUSFM.manifestyaml = FakeManifestYaml()
    verifyUSFM.suppress = [False] * 13
    verifyUSFM.verifyDir(directory)
    return verifyUSFM.listener.result.results