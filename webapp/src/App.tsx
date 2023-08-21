// Copyright (c) Microsoft. All rights reserved.

import { AuthenticatedTemplate, UnauthenticatedTemplate, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { FluentProvider, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import * as React from 'react';
import { FC, useEffect } from 'react';
import { UserSettingsMenu } from './components/header/UserSettingsMenu';
import { BackendProbe, ChatView, Error, Loading, Login } from './components/views';
import { useChat } from './libs/hooks';
import { AlertType } from './libs/models/AlertType';
import { useAppDispatch, useAppSelector } from './redux/app/hooks';
import { RootState } from './redux/app/store';
import { FeatureKeys } from './redux/features/app/AppState';
import { addAlert, setActiveUserInfo, setServiceOptions } from './redux/features/app/appSlice';
import { semanticKernelDarkTheme, semanticKernelLightTheme } from './styles';

import playFabLogo from './assets/playfab-icons/playfab-mark.png';

export const useClasses = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100vh',
        width: '100%',
        ...shorthands.overflow('hidden'),
    },
    header: {
        alignItems: 'center',
        backgroundColor: tokens.colorBrandForeground2,
        color: tokens.colorNeutralForegroundOnBrand,
        display: 'flex',
        '& div': {
            display: 'flex',
            alignItems: 'center',
        },
        height: '48px',
        justifyContent: 'space-between',
        width: '100%',
    },
    logo: {
        width: '23px',
        height: '26px',
        backgroundSize: '23px 26px',
        paddingLeft: '23px',
    },
    title: {
        height: '24px',
        marginLeft: '12px',
        paddingLeft: '12px',
        fontSize: '16px',
        fontWeight: 'normal',
        borderLeftColor: 'white',
        borderLeftWidth: '1px',
        borderLeftStyle: 'solid',
        lineHeight: '20px',
    },
    persona: {
        marginRight: tokens.spacingHorizontalXXL,
    },
    cornerItems: {
        display: 'flex',
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
});

enum AppState {
    ProbeForBackend,
    SettingUserInfo,
    ErrorLoadingUserInfo,
    LoadingChats,
    Chat,
    SigningOut,
}

const App: FC = () => {
    const classes = useClasses();

    const [appState, setAppState] = React.useState(AppState.ProbeForBackend);
    const dispatch = useAppDispatch();

    const { instance, inProgress } = useMsal();
    const { activeUserInfo, features } = useAppSelector((state: RootState) => state.app);
    const isAuthenticated = useIsAuthenticated();

    const chat = useChat();

    useEffect(() => {
        if (isAuthenticated) {
            if (appState === AppState.SettingUserInfo) {
                if (activeUserInfo === undefined) {
                    const account = instance.getActiveAccount();
                    if (!account) {
                        setAppState(AppState.ErrorLoadingUserInfo);
                    } else {
                        dispatch(
                            setActiveUserInfo({
                                id: account.homeAccountId,
                                email: account.username, // username in an AccountInfo object is the email address
                                username: account.name ?? account.username,
                            }),
                        );

                        // Privacy disclaimer for internal Microsoft users
                        if (account.username.split('@')[1] === 'microsoft.com') {
                            dispatch(
                                addAlert({
                                    message:
                                        'By using Chat Copilot, you agree to protect sensitive data, not store it in chat, and allow chat history collection for service improvements. This tool is for internal use only.',
                                    type: AlertType.Info,
                                }),
                            );
                        }

                        setAppState(AppState.LoadingChats);
                    }
                } else {
                    setAppState(AppState.LoadingChats);
                }
            }

            if (appState === AppState.LoadingChats) {
                void Promise.all([
                    // Load all chats from memory
                    chat.loadChats().then((succeeded) => {
                        if (succeeded) {
                            setAppState(AppState.Chat);
                        }
                    }),

                    // Load service options
                    chat.getServiceOptions().then((serviceOptions) => {
                        if (serviceOptions) {
                            dispatch(setServiceOptions(serviceOptions));
                        }
                    }),
                ]);
            }
        }

        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [instance, inProgress, isAuthenticated, appState]);

    // TODO: [Issue #41] handle error case of missing account information
    return (
        <FluentProvider
            className="app-container"
            theme={features[FeatureKeys.DarkMode].enabled ? semanticKernelDarkTheme : semanticKernelLightTheme}
        >
            <UnauthenticatedTemplate>
                <div className={classes.container}>
                    <div className={classes.header}>
                        <div>
                            <img className={classes.logo} src={playFabLogo} />
                            <h1 className={classes.title}>PlayFab Copilot</h1>
                        </div>
                    </div>
                    {appState === AppState.SigningOut && <Loading text="Signing you out..." />}
                    {appState !== AppState.SigningOut && <Login />}
                </div>
            </UnauthenticatedTemplate>
            <AuthenticatedTemplate>
                <div className={classes.container}>
                    <div className={classes.header}>
                        <div>
                            <img className={classes.logo} src={playFabLogo} />
                            <h1 className={classes.title}>PlayFab Copilot</h1>
                        </div>
                        {appState > AppState.SettingUserInfo && (
                            <div className={classes.cornerItems}>
                                <div data-testid="logOutMenuList" className={classes.cornerItems}>
                                    <UserSettingsMenu
                                        setLoadingState={() => {
                                            setAppState(AppState.SigningOut);
                                        }}
                                    />
                                </div>
                            </div>
                        )}
                    </div>
                    {appState === AppState.ProbeForBackend && (
                        <BackendProbe
                            uri={process.env.REACT_APP_BACKEND_URI as string}
                            onBackendFound={() => {
                                setAppState(AppState.SettingUserInfo);
                            }}
                        />
                    )}
                    {appState === AppState.SettingUserInfo && (
                        <Loading text={'Hang tight while we fetch your information...'} />
                    )}
                    {appState === AppState.ErrorLoadingUserInfo && (
                        <Error text={'Oops, something went wrong. Please try signing out and signing back in.'} />
                    )}
                    {appState === AppState.LoadingChats && <Loading text="Loading Chats..." />}
                    {appState === AppState.Chat && <ChatView />}
                </div>
            </AuthenticatedTemplate>
        </FluentProvider>
    );
};

export default App;
