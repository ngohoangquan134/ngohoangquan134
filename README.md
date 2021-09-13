from kivy.app import App
from kivy.uix.screenmanager import ScreenManager, Screen
from kivy.lang import Builder
from collections import OrderedDict
import pymongo 
from pymongo import MongoClient
MongoClient.connect(Config.database.url, {
    server: {
        socketOptions: {
            connectTimeoutMS: 5000
        }
    }
}).then((db) => {
    console.log('Connected to MongoDB');
    databaseInstance = db;
   // start web server
})

print(to_print)

class FirstWindow(Screen):
    pass


class SecondWindow(Screen):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)

    def validate_user(self):
        ten = self.ids.ten_field
        mk = self.ids.mk_field
        info = self.ids.info

        tendung = ten.text
        mkhau = mk.text

        if tendung == '' or mkhau == '':
            info.text = '[color=#FF0000]Vui lòng nhập Tài khoản và mật khẩu[/color]'
        else:
            if tendung == 'user' and mkhau == 'user':
                FourWindow().run()
            if tendung == 'admin' and mkhau == 'admin':
                info.text = '[color=#00FF00]tài khoản đăng nhập chính xác[/color]'
            else:
                info.text = '[color=#FF0000]Sai mật khẩu hoặc tài khoản[/color]'


class ThirdWindow(Screen):
    pass


class FourWindow(Screen):
    def __init__(self, **kw):
        super().__init__(**kw)

    client = MongoClient('mongodb+srv://luutruthuvien:mybPvHetSVNDaQfm@cluster0.6nrqa.mongodb.net/myFirstDatabase?retryWrites=true&w=majority')
    db = client.silverpos
    users = db.users
    _users = OrderedDict(
        first_name={}
        last_name={}
        user_name={}
        passwords={}
        designations={}

    )
    first_name = []
    last_name = []
    user_name = []
    passwords = []
    designations = []

    for user in users.find():
        first_name.append(user['fisrt_name'])
        last_name.append(user['last_name'])
        user_name.append(user['user_name'])
        passwords.append(user['passwords'])
        designations.append(user['designations'])
    # print(designations)
    users_length = len(first_name)
    idx


class WindowManager(ScreenManager):
    pass



